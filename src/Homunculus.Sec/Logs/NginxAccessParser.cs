using System.Globalization;
using System.Text.RegularExpressions;

namespace Homunculus.Sec.Logs;

/// <summary>Parses nginx's default "combined" access log format.</summary>
public sealed partial class NginxAccessParser : ILogParser
{
    public LogSource Source => LogSource.NginxAccess;

    [GeneratedRegex("""^(?<ip>\S+) \S+ \S+ \[(?<time>[^\]]+)\] "(?:(?<method>\S+) (?<path>\S+)[^"]*|[^"]*)" (?<status>\d{3}) (?:\d+|-)(?: "[^"]*" "(?<agent>[^"]*)")?""")]
    private static partial Regex Line();

    public LogEntry? Parse(string line)
    {
        var match = Line().Match(line);
        if (!match.Success) return null;
        if (!DateTimeOffset.TryParseExact(match.Groups["time"].Value, "dd/MMM/yyyy:HH:mm:ss zzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return null;

        return new LogEntry(
            LogSource.NginxAccess,
            EntryKind.Request,
            time,
            match.Groups["ip"].Value,
            line,
            Method: NullIfEmpty(match.Groups["method"]),
            Path: NullIfEmpty(match.Groups["path"]),
            Status: int.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture),
            UserAgent: NullIfEmpty(match.Groups["agent"]));
    }

    private static string? NullIfEmpty(Group group) => group.Success && group.Length > 0 ? group.Value : null;
}
