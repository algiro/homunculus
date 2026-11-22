using System.Globalization;
using System.Text;
using Homunculus.Sec.Logs;
using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Detection;

/// <summary>
/// Sees an attack that no single address shows: several addresses of one network (a /24, a /64 for IPv6), each too quiet to trip a
/// per-address rule, together making many attempts. Fires once per network per cooldown, when at least <c>minAddresses</c> distinct
/// addresses and at least <c>minEvents</c> matching events fall inside the window. The finding's address is the network
/// (<c>203.0.113.0/24</c>), and a summary lists how many addresses and attempts and which addresses were busiest.
///
/// If an address of the same network logged in successfully during the window, the finding carries a caution: blocking the network
/// would cut that user off. Windows follow the entries' own timestamps. Not thread-safe: one consumer feeds it.
/// </summary>
public sealed class NetworkDetector(string rule, Func<LogEntry, bool> matches, int minAddresses, int minEvents, TimeSpan window, TimeSpan cooldown, int minEventsPerAddress = 3) : IDetector
{
    private const int MaxEventsPerAddress = 500;
    private const int MaxSamples = 5;
    private const int PruneEvery = 1000;

    private sealed class State
    {
        public Dictionary<string, Queue<DateTimeOffset>> Events { get; } = [];
        public Dictionary<string, DateTimeOffset> Logins { get; } = [];
        public Queue<string> Samples { get; } = new();
        public DateTimeOffset? LastFired { get; set; }
    }

    private readonly Dictionary<string, State> _byNetwork = [];
    private int _observed;

    public Finding? Observe(LogEntry entry)
    {
        if (entry.Ip is not { } ip || Cidr.GroupOf(ip) is not { } network) return null;

        if (++_observed % PruneEvery == 0) Prune(entry.Time);

        if (entry.Kind == EntryKind.LoginSuccess)
        {
            if (_byNetwork.TryGetValue(network, out var known)) known.Logins[ip] = entry.Time;
            else (_byNetwork[network] = new State()).Logins[ip] = entry.Time;
            return null;
        }

        if (!matches(entry)) return null;

        if (!_byNetwork.TryGetValue(network, out var state)) _byNetwork[network] = state = new State();
        if (!state.Events.TryGetValue(ip, out var times)) state.Events[ip] = times = new Queue<DateTimeOffset>();
        times.Enqueue(entry.Time);
        while (times.Count > MaxEventsPerAddress) times.Dequeue();
        state.Samples.Enqueue(entry.Raw);
        while (state.Samples.Count > MaxSamples) state.Samples.Dequeue();

        Expire(state, entry.Time);
        // An address that made one or two attempts is a bystander, not a member of the attack.
        var active = state.Events.Where(pair => pair.Value.Count >= minEventsPerAddress).ToList();
        var addresses = active.Count;
        var total = active.Sum(pair => pair.Value.Count);
        if (addresses < minAddresses || total < minEvents) return null;
        if (state.LastFired is { } last && entry.Time - last < cooldown) return null;

        state.LastFired = entry.Time;
        var caution = state.Logins.Count == 0
            ? null
            : $"{state.Logins.Count} address(es) of this network logged in successfully within the window. Blocking the whole network would cut them off.";
        return new Finding(rule, network, entry.Time, total, window, state.Samples.ToArray(), Summarise(network, active, addresses, total), Hard: false, caution);
    }

    private string Summarise(string network, List<KeyValuePair<string, Queue<DateTimeOffset>>> active, int addresses, int total)
    {
        var busiest = active
            .OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(4)
            .Select(pair => $"{pair.Key} ({pair.Value.Count})");

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"Network {network}: {addresses} different addresses, each with at least {minEventsPerAddress} attempts, made {total} failed or dropped login attempts within {window.TotalHours:0} h (the monitor alerts from {minAddresses} addresses and {minEvents} attempts).");
        text.AppendLine("Busiest addresses: " + string.Join(", ", busiest) + ".");
        text.Append("Several addresses of one network attacking together suggests a single actor spreading its attempts.");
        return text.ToString();
    }

    private void Expire(State state, DateTimeOffset now)
    {
        foreach (var ip in state.Events.Keys.ToList())
        {
            var times = state.Events[ip];
            while (times.Count > 0 && now - times.Peek() > window) times.Dequeue();
            if (times.Count == 0) state.Events.Remove(ip);
        }

        foreach (var ip in state.Logins.Where(pair => now - pair.Value > window).Select(pair => pair.Key).ToList()) state.Logins.Remove(ip);
    }

    /// <summary>Forgets networks that have been quiet for longer than the window and the cooldown, so memory stays bounded.</summary>
    private void Prune(DateTimeOffset now)
    {
        var keep = window > cooldown ? window : cooldown;
        var stale = _byNetwork
            .Where(pair => pair.Value.LastFired is not { } fired || now - fired > keep)
            .Where(pair => pair.Value.Events.Values.All(q => q.Count == 0 || now - q.Last() > keep) && pair.Value.Logins.Values.All(t => now - t > keep))
            .Select(pair => pair.Key)
            .ToList();
        foreach (var network in stale) _byNetwork.Remove(network);
    }
}
