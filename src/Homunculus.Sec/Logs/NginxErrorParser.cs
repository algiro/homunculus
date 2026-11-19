using System.Globalization;
using System.Text.RegularExpressions;

namespace Homunculus.Sec.Logs;

/// <summary>Parses nginx's error log. Lines are classed as rate-limit hits or generic server errors, with the client IP when present.</summary>
public sealed partial class NginxErrorParser : ILogParser
{
    public LogSource Source => LogSource.NginxError;

    [GeneratedRegex(@"^(?<time>\d{4}/\d\d/\d\d \d\d:\d\d:\d\d) \[(?<level>\w+)\] \d+#\d+: (?:\*\d+ )?(?<message>.*)$")]
    private static partial Regex Line();

    [GeneratedRegex(@"client: (?<ip>[0-9a-fA-F:.]+)")]
    private static partial Regex Client();

    public LogEntry? Parse(string line)
    {
        var match = Line().Match(line);
        if (!match.Success) return null;
        if (!DateTime.TryParseExact(match.Groups["time"].Value, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        var message = match.Groups["message"].Value;
        var client = Client().Match(message);
        var kind = message.Contains("limiting requests", StringComparison.Ordinal) || message.Contains("limiting connections", StringComparison.Ordinal)
            ? EntryKind.RateLimited
            : EntryKind.ServerError;

        return new LogEntry(LogSource.NginxError, kind, new DateTimeOffset(local), client.Success ? client.Groups["ip"].Value : null, line);
    }
}
