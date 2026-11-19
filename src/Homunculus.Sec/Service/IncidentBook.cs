using Homunculus.Sec.Analysis;

namespace Homunculus.Sec.Service;

public enum IncidentStatus { Pending, Approved, Dismissed }

/// <summary>A proposal waiting for the operator's answer, and the Telegram message that carries it.</summary>
public sealed class Incident(string id, Proposal proposal)
{
    public string Id { get; } = id;
    public Proposal Proposal { get; } = proposal;
    public IncidentStatus Status { get; set; } = IncidentStatus.Pending;
    public long? MessageId { get; set; }
}

/// <summary>The incidents of this run, newest last. Only the most recent ones are kept.</summary>
public sealed class IncidentBook(int capacity = 200)
{
    private readonly List<Incident> _incidents = [];
    private int _next;

    public Incident Add(Proposal proposal)
    {
        lock (_incidents)
        {
            var incident = new Incident((++_next).ToString(System.Globalization.CultureInfo.InvariantCulture), proposal);
            _incidents.Add(incident);
            if (_incidents.Count > capacity) _incidents.RemoveAt(0);
            return incident;
        }
    }

    public Incident? Find(string id)
    {
        lock (_incidents) return _incidents.FirstOrDefault(i => i.Id == id);
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
}
