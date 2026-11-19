using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Detection;

public interface IDetector
{
    /// <summary>Feeds one entry in; returns a finding when the entry makes the rule fire.</summary>
    Finding? Observe(LogEntry entry);
}
