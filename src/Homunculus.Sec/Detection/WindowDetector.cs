using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Detection;

/// <summary>
/// Fires when one IP produces <c>threshold</c> matching entries within <c>window</c>, at most once per <c>cooldown</c> per IP.
/// Windows follow the entries' own timestamps, so replaying a log gives the same result. Not thread-safe: one consumer feeds it.
/// </summary>
public sealed class WindowDetector(string rule, Func<LogEntry, bool> matches, int threshold, TimeSpan window, TimeSpan cooldown) : IDetector
{
    private const int MaxSamples = 5;
    private const int PruneEvery = 1000;

    private sealed class State
    {
        public Queue<DateTimeOffset> Times { get; } = new();
        public Queue<string> Samples { get; } = new();
        public DateTimeOffset? LastFired { get; set; }
    }

    private readonly Dictionary<string, State> _byIp = [];
    private int _observed;

    public Finding? Observe(LogEntry entry)
    {
        if (entry.Ip is not { } ip || !matches(entry)) return null;

        if (++_observed % PruneEvery == 0) Prune(entry.Time);

        if (!_byIp.TryGetValue(ip, out var state)) _byIp[ip] = state = new State();
        state.Times.Enqueue(entry.Time);
        state.Samples.Enqueue(entry.Raw);
        while (state.Samples.Count > MaxSamples) state.Samples.Dequeue();
        while (state.Times.Count > 0 && entry.Time - state.Times.Peek() > window) state.Times.Dequeue();

        if (state.Times.Count < threshold) return null;
        if (state.LastFired is { } last && entry.Time - last < cooldown) return null;

        state.LastFired = entry.Time;
        return new Finding(rule, ip, entry.Time, state.Times.Count, window, state.Samples.ToArray());
    }

    /// <summary>Forgets IPs that have been quiet for longer than the window and the cooldown, so memory stays bounded.</summary>
    private void Prune(DateTimeOffset now)
    {
        var keep = window > cooldown ? window : cooldown;
        var stale = _byIp
            .Where(pair => pair.Value.Times.Count == 0 || now - pair.Value.Times.Last() > keep)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var ip in stale) _byIp.Remove(ip);
    }
}
