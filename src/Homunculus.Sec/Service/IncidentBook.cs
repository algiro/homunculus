using Homunculus.Sec.Analysis;
using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Service;

public enum IncidentStatus { Pending, Approved, Dismissed }

/// <summary>A proposal waiting for the operator's answer, and the Telegram message that carries it.</summary>
public sealed class Incident(string id, string token, Proposal proposal)
{
    /// <summary>The short number shown to the operator (#7). It starts again at 1 whenever the service restarts.</summary>
    public string Id { get; } = id;

    /// <summary>
    /// What the buttons carry: the run's random token plus the number. Unlike <see cref="Id"/> it is unique across restarts, so a button
    /// on an old message can never be mistaken for a button of a newer alert that reused the number.
    /// </summary>
    public string Token { get; } = token;
    public Proposal Proposal { get; } = proposal;
    public IncidentStatus Status { get; set; } = IncidentStatus.Pending;
    public long? MessageId { get; set; }
}

/// <summary>The incidents of this run, newest last. Only the most recent ones are kept.</summary>
public sealed class IncidentBook(int capacity = 200, string? runToken = null)
{
    private readonly string _run = runToken ?? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
    private readonly List<Incident> _incidents = [];
    private int _next;

    public Incident Add(Proposal proposal)
    {
        lock (_incidents)
        {
            var number = (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var incident = new Incident(number, $"{_run}.{number}", proposal);
            _incidents.Add(incident);
            if (_incidents.Count > capacity) _incidents.RemoveAt(0);
            return incident;
        }
    }

    /// <summary>Finds an alert by the token its buttons carry; null for an alert of an earlier run or one that is no longer kept.</summary>
    public Incident? Find(string token)
    {
        lock (_incidents) return _incidents.FirstOrDefault(i => i.Token == token);
    }

    public Incident? FindByMessage(long messageId)
    {
        lock (_incidents) return _incidents.FirstOrDefault(i => i.MessageId == messageId);
    }

    public Incident? LatestPending()
    {
        lock (_incidents) return _incidents.LastOrDefault(i => i.Status == IncidentStatus.Pending);
    }

    public IReadOnlyList<Incident> Pending()
    {
        lock (_incidents) return _incidents.Where(i => i.Status == IncidentStatus.Pending).ToList();
    }

    public bool HasPendingFor(string ip)
    {
        lock (_incidents) return _incidents.Any(i => i.Status == IncidentStatus.Pending && i.Proposal.Ip == ip);
    }

    /// <summary>True when an alert waiting for an answer already concerns this address or network, or a network that contains it.</summary>
    public bool HasPendingCovering(string target)
    {
        lock (_incidents) return _incidents.Any(i => i.Status == IncidentStatus.Pending && (i.Proposal.Ip == target || Cidr.Covers(i.Proposal.Ip, target)));
    }
}
