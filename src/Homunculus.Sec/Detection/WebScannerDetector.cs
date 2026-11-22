using System.Globalization;
using System.Text;
using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Detection;

/// <summary>
/// Scores each source IP on the web server's access log. Different kinds of suspicious behaviour add points, each distinct thing
/// once, and points expire after the window. When the score reaches the threshold the IP is reported, with a summary of what it
/// did that the analyst can read. This finds scanners that no fixed list or rate threshold would: a slow wordlist crawl, a probe that
/// the site answers with a redirect, a mix of small oddities.
///
/// <list type="bullet">
/// <item>a distinct path that failed (status 400 or more): 1 point each (the usual misses such as favicon.ico do not count)</item>
/// <item>a malformed request (status 400): 2</item>
/// <item>an unusual method, or a request line that cannot be read (CONNECT, PRI, binary junk): 3 each kind</item>
/// <item>a path asked for by scanners (/.env, wp-login.php, phpmyadmin…): 4 each</item>
/// <item>a traversal or injection string: 5 each</item>
/// <item>a scanner-like user agent: 1</item>
/// </list>
///
/// Windows follow the entries' own timestamps. Not thread-safe: one consumer feeds it.
/// </summary>
public sealed class WebScannerDetector(string rule, int threshold, TimeSpan window, TimeSpan cooldown) : IDetector
{
    private const int MaxSignalsPerIp = 200;
    private const int MaxRequestsPerIp = 1000;
    private const int MaxSamples = 5;
    private const int PruneEvery = 1000;

    private enum Kind { FailingPath, BadRequest, OddMethod, ProbePath, Injection, ScannerAgent }

    private sealed record Seen(DateTimeOffset Time, int Weight, Kind Kind, string Example);

    private sealed record Request(DateTimeOffset Time, int Status, string UserAgent);

    private sealed class State
    {
        public Dictionary<string, Seen> Signals { get; } = [];
        public Queue<Request> Requests { get; } = new();
        public Queue<string> Samples { get; } = new();
        public DateTimeOffset? LastFired { get; set; }
    }

    private readonly Dictionary<string, State> _byIp = [];
    private int _observed;

    public Finding? Observe(LogEntry entry)
    {
        if (entry is not { Source: LogSource.NginxAccess, Kind: EntryKind.Request, Ip: { } ip }) return null;

        if (++_observed % PruneEvery == 0) Prune(entry.Time);

        if (!_byIp.TryGetValue(ip, out var state)) _byIp[ip] = state = new State();

        foreach (var (key, seen) in SignalsOf(entry))
        {
            if (state.Signals.Count < MaxSignalsPerIp || state.Signals.ContainsKey(key)) state.Signals[key] = seen;
        }

        state.Requests.Enqueue(new Request(entry.Time, entry.Status ?? 0, entry.UserAgent ?? ""));
        while (state.Requests.Count > MaxRequestsPerIp) state.Requests.Dequeue();
        state.Samples.Enqueue(entry.Raw);
        while (state.Samples.Count > MaxSamples) state.Samples.Dequeue();

        Expire(state, entry.Time);
        var score = state.Signals.Values.Sum(s => s.Weight);
        if (score < threshold) return null;
        if (state.LastFired is { } last && entry.Time - last < cooldown) return null;

        state.LastFired = entry.Time;
        var hard = state.Signals.Values.Any(s => s.Kind is Kind.Injection or Kind.ProbePath or Kind.OddMethod);
        return new Finding(rule, ip, entry.Time, score, window, state.Samples.ToArray(), Summarise(state, score), hard);
    }

    private static IEnumerable<KeyValuePair<string, Seen>> SignalsOf(LogEntry entry)
    {
        var time = entry.Time;
        var path = entry.Path is null ? null : ScannerPaths.Normalize(entry.Path);

        if (entry.Method is null)
            yield return Signal("method:-", time, 3, Kind.OddMethod, "unreadable request line");
        else if (!ScannerPaths.IsStandardMethod(entry.Method))
            yield return Signal("method:" + Truncate(entry.Method, 12), time, 3, Kind.OddMethod, Truncate(entry.Method, 12));

        if (entry.Status == 400) yield return Signal("bad-request", time, 2, Kind.BadRequest, "400");

        if (path is not null)
        {
            if (entry.Status >= 400 && !ScannerPaths.IsExpectedMiss(path)) yield return Signal("fail:" + path, time, 1, Kind.FailingPath, Truncate(path, 60));
            if (ScannerPaths.IsProbe(path)) yield return Signal("probe:" + path, time, 4, Kind.ProbePath, Truncate(path, 60));
            if (ScannerPaths.LooksLikeInjection(entry.Path!)) yield return Signal("inject:" + path, time, 5, Kind.Injection, Truncate(path, 60));
        }

        if (ScannerPaths.IsScannerAgent(entry.UserAgent)) yield return Signal("agent", time, 1, Kind.ScannerAgent, Truncate(entry.UserAgent!, 60));
    }

    private static KeyValuePair<string, Seen> Signal(string key, DateTimeOffset time, int weight, Kind kind, string example) =>
        new(key, new Seen(time, weight, kind, example));

    private void Expire(State state, DateTimeOffset now)
    {
        foreach (var key in state.Signals.Where(pair => now - pair.Value.Time > window).Select(pair => pair.Key).ToList())
            state.Signals.Remove(key);
        while (state.Requests.Count > 0 && now - state.Requests.Peek().Time > window) state.Requests.Dequeue();
    }

    /// <summary>What the analyst reads: the score, the traffic mix and each kind of signal with a few examples. Built from log text, so it is cut short and kept to single lines.</summary>
    private string Summarise(State state, int score)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Suspicion score {score} within {window.TotalMinutes:0} min (the monitor alerts from {threshold}).");

        var requests = state.Requests.ToList();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Requests in that window: {requests.Count}; responses 2xx {requests.Count(r => r.Status / 100 == 2)}, 3xx {requests.Count(r => r.Status / 100 == 3)}, 4xx {requests.Count(r => r.Status / 100 == 4)}, 5xx {requests.Count(r => r.Status / 100 == 5)}.");

        var signals = state.Signals.Values.ToList();
        var parts = new List<string>();
        AddKind(parts, signals, Kind.FailingPath, "distinct failing paths");
        AddKind(parts, signals, Kind.ProbePath, "paths that scanners ask for");
        AddKind(parts, signals, Kind.Injection, "traversal or injection strings");
        AddKind(parts, signals, Kind.OddMethod, "unusual methods or unreadable requests");
        AddKind(parts, signals, Kind.BadRequest, "malformed requests");
        AddKind(parts, signals, Kind.ScannerAgent, "scanner-like user agent");
        text.AppendLine("Signals: " + string.Join("; ", parts) + ".");

        var agents = requests.Select(r => r.UserAgent).Where(a => a.Length > 0 && a != "-")
            .GroupBy(a => a).OrderByDescending(g => g.Count()).Take(2)
            .Select(g => $"{Truncate(g.Key, 60)} ({g.Count()})");
        text.Append("User agents: " + (agents.Any() ? string.Join(", ", agents) : "none sent") + ".");
        return text.ToString();
    }

    private static void AddKind(List<string> parts, List<Seen> signals, Kind kind, string label)
    {
        var of = signals.Where(s => s.Kind == kind).ToList();
        if (of.Count == 0) return;
        var examples = string.Join(", ", of.Select(s => s.Example).Distinct().Take(4));
        parts.Add($"{of.Count} {label} (e.g. {examples})");
    }

    /// <summary>Text from the log ends up in the analyst's input, so control characters (a newline that fakes a new line) become spaces and the length is capped.</summary>
    private static string Truncate(string text, int max)
    {
        var tidy = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return tidy.Length <= max ? tidy : tidy[..max];
    }

    /// <summary>Forgets IPs that have been quiet for longer than the window and the cooldown, so memory stays bounded.</summary>
    private void Prune(DateTimeOffset now)
    {
        var keep = window > cooldown ? window : cooldown;
        var stale = _byIp
            .Where(pair => pair.Value.Requests.Count == 0 && (pair.Value.LastFired is null || now - pair.Value.LastFired > keep)
                           || pair.Value.Requests.Count > 0 && now - pair.Value.Requests.Last().Time > keep)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var ip in stale) _byIp.Remove(ip);
    }
}
