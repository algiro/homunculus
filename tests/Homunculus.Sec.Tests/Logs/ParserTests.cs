using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Tests.Logs;

public class ParserTests
{
    [Test]
    public void NginxAccess_ParsesACombinedLine()
    {
        const string line = """203.0.113.9 - - [19/Nov/2026:10:15:30 +0000] "GET /.env HTTP/1.1" 404 153 "-" "curl/8.5.0" """;

        var entry = new NginxAccessParser().Parse(line)!;

        Assert.That(entry.Kind, Is.EqualTo(EntryKind.Request));
        Assert.That(entry.Ip, Is.EqualTo("203.0.113.9"));
        Assert.That(entry.Method, Is.EqualTo("GET"));
        Assert.That(entry.Path, Is.EqualTo("/.env"));
        Assert.That(entry.Status, Is.EqualTo(404));
        Assert.That(entry.UserAgent, Is.EqualTo("curl/8.5.0"));
        Assert.That(entry.Time, Is.EqualTo(new DateTimeOffset(2026, 11, 19, 10, 15, 30, TimeSpan.Zero)));
    }

    [Test]
    public void NginxAccess_AcceptsAMalformedRequestLine()
    {
        const string line = """203.0.113.9 - - [19/Nov/2026:10:15:30 +0000] "\x16\x03\x01" 400 150 "-" "-" """;

        var entry = new NginxAccessParser().Parse(line)!;

        Assert.That(entry.Status, Is.EqualTo(400));
        Assert.That(entry.Path, Is.Null);
    }

    [Test]
    public void NginxAccess_IgnoresGarbage() => Assert.That(new NginxAccessParser().Parse("not a log line"), Is.Null);

    [Test]
    public void NginxError_RecognisesRateLimiting()
    {
        const string line = """2026/11/19 10:15:30 [error] 812#812: *44 limiting requests, excess: 5.210 by zone "api", client: 198.51.100.7, server: example.com, request: "GET /api HTTP/1.1", host: "example.com" """;

        var entry = new NginxErrorParser().Parse(line)!;

        Assert.That(entry.Kind, Is.EqualTo(EntryKind.RateLimited));
        Assert.That(entry.Ip, Is.EqualTo("198.51.100.7"));
    }

    [Test]
    public void NginxError_OtherErrorsAreServerErrors()
    {
        const string line = "2026/11/19 10:15:30 [crit] 812#812: *44 connect() to unix:/run/app.sock failed (2: No such file or directory), client: 198.51.100.7";

        Assert.That(new NginxErrorParser().Parse(line)!.Kind, Is.EqualTo(EntryKind.ServerError));
    }

    [Test]
    public void Auth_ParsesAFailedPasswordInIsoFormat()
    {
        const string line = "2026-11-19T10:15:30.123456+00:00 web sshd[1234]: Failed password for invalid user admin from 192.0.2.44 port 51022 ssh2";

        var entry = new AuthLogParser().Parse(line)!;

        Assert.That(entry.Kind, Is.EqualTo(EntryKind.FailedLogin));
        Assert.That(entry.Ip, Is.EqualTo("192.0.2.44"));
        Assert.That(entry.User, Is.EqualTo("admin"));
    }

    [Test]
    public void Auth_ParsesSyslogTimestampsUsingTheClockYear()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
        const string line = "Nov  9 10:15:30 web sshd[1234]: Accepted publickey for deploy from 192.0.2.10 port 40000 ssh2";

        var entry = new AuthLogParser(clock).Parse(line)!;

        Assert.That(entry.Kind, Is.EqualTo(EntryKind.LoginSuccess));
        Assert.That(entry.Time.Year, Is.EqualTo(2026));
        Assert.That(entry.Time.Day, Is.EqualTo(9));
    }

    [Test]
    public void Auth_ADecemberLineReadInJanuaryBelongsToLastYear()
    {
        var clock = new FixedClock(new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero));
        const string line = "Dec 31 23:59:59 web sshd[1]: Failed password for root from 192.0.2.1 port 1 ssh2";

        Assert.That(new AuthLogParser(clock).Parse(line)!.Time.Year, Is.EqualTo(2026));
    }

    [Test]
    public void Auth_IgnoresOtherPrograms() =>
        Assert.That(new AuthLogParser().Parse("2026-11-19T10:15:30+00:00 web sudo: alex : TTY=pts/0 ; COMMAND=/bin/ls"), Is.Null);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
