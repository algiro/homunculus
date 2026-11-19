namespace Homunculus.Sec.Logs;

public interface ILogParser
{
    LogSource Source { get; }

    /// <summary>Returns null for a line that is not (or not an interesting) entry of this log.</summary>
    LogEntry? Parse(string line);
}
