using System.Globalization;
using System.Text.RegularExpressions;

namespace Homunculus.Sec.Logs;

/// <summary>Parses sshd lines of /var/log/auth.log, in both the classic syslog and the ISO-8601 timestamp styles.</summary>
public sealed partial class AuthLogParser(TimeProvider? clock = null) : ILogParser
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public LogSource Source => LogSource.Auth;

    [GeneratedRegex(@"^(?<time>\d{4}-\d\d-\d\dT\S+|[A-Z][a-z]{2} +\d+ \d\d:\d\d:\d\d) \S+ sshd(?:\[\d+\])?: (?<message>.*)$")]
    private static partial Regex Line();

    [GeneratedRegex(@"^Failed (?:password|publickey) for (?:invalid user )?(?<user>\S+) from (?<ip>\S+) port")]
    private static partial Regex Failed();

    [GeneratedRegex(@"^Invalid user (?<user>\S*) from (?<ip>\S+)")]
    private static partial Regex Invalid();

    [GeneratedRegex(@"^Accepted \S+ for (?<user>\S+) from (?<ip>\S+) port")]
    private static partial Regex Accepted();

    public LogEntry? Parse(string line)
    {
        var match = Line().Match(line);
        if (!match.Success) return null;
        var time = ParseTime(match.Groups["time"].Value);
        if (time is null) return null;

        var message = match.Groups["message"].Value;
        foreach (var (regex, kind) in new[] { (Failed(), EntryKind.FailedLogin), (Invalid(), EntryKind.InvalidUser), (Accepted(), EntryKind.LoginSuccess) })
        {
            var found = regex.Match(message);
            if (found.Success)
                return new LogEntry(LogSource.Auth, kind, time.Value, found.Groups["ip"].Value, line, User: found.Groups["user"].Value);
        }

        return null;
    }

    private DateTimeOffset? ParseTime(string text)
    {
        if (text.Contains('T', StringComparison.Ordinal))
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso) ? iso : null;

        var now = _clock.GetLocalNow();
        var squeezed = SpaceRun().Replace(text, " ");
        if (!DateTime.TryParseExact($"{now.Year} {squeezed}", "yyyy MMM d HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        var stamped = new DateTimeOffset(local, now.Offset);
        // A December line read in January belongs to last year.
        return stamped > now.AddDays(1) ? stamped.AddYears(-1) : stamped;
    }

    [GeneratedRegex(" +")]
    private static partial Regex SpaceRun();
}
