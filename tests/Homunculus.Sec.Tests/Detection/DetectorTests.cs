using Homunculus.Sec.Detection;
using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Tests.Detection;

public class DetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static LogEntry Request(string ip, double seconds, int status = 200, string path = "/") =>
        new(LogSource.NginxAccess, EntryKind.Request, Start.AddSeconds(seconds), ip, $"{ip} {path} {status}", Path: path, Status: status);

    private static LogEntry FailedLogin(string ip, double seconds) =>
        new(LogSource.Auth, EntryKind.FailedLogin, Start.AddSeconds(seconds), ip, $"failed {ip}", User: "root");

    private static WindowDetector Detector(int threshold = 3, int windowSeconds = 10, int cooldownSeconds = 60) =>
        new("test", _ => true, threshold, TimeSpan.FromSeconds(windowSeconds), TimeSpan.FromSeconds(cooldownSeconds));

    [Test]
    public void Fires_WhenTheThresholdIsReachedInsideTheWindow()
    {
        var detector = Detector();

        Assert.That(detector.Observe(Request("1.1.1.1", 0)), Is.Null);
        Assert.That(detector.Observe(Request("1.1.1.1", 1)), Is.Null);
        var finding = detector.Observe(Request("1.1.1.1", 2))!;

        Assert.That(finding.Ip, Is.EqualTo("1.1.1.1"));
        Assert.That(finding.Count, Is.EqualTo(3));
        Assert.That(finding.Samples, Has.Count.EqualTo(3));
    }

    [Test]
    public void DoesNotFire_WhenTheEventsAreSpreadOut()
    {
        var detector = Detector();

        for (var i = 0; i < 20; i++)
            Assert.That(detector.Observe(Request("1.1.1.1", i * 6)), Is.Null);
    }

    [Test]
    public void CountsEachIpSeparately()
    {
        var detector = Detector();

        Assert.That(detector.Observe(Request("1.1.1.1", 0)), Is.Null);
        Assert.That(detector.Observe(Request("2.2.2.2", 1)), Is.Null);
        Assert.That(detector.Observe(Request("1.1.1.1", 2)), Is.Null);
        Assert.That(detector.Observe(Request("2.2.2.2", 3)), Is.Null);
    }

    [Test]
    public void StaysQuietDuringTheCooldownThenFiresAgain()
    {
        var detector = Detector();
        for (var i = 0; i < 3; i++) detector.Observe(Request("1.1.1.1", i));

        Assert.That(detector.Observe(Request("1.1.1.1", 3)), Is.Null);
        Assert.That(detector.Observe(Request("1.1.1.1", 4)), Is.Null);

        for (var i = 0; i < 2; i++) detector.Observe(Request("1.1.1.1", 100 + i));
        Assert.That(detector.Observe(Request("1.1.1.1", 102)), Is.Not.Null);
    }

    [Test]
    public void KeepsOnlyTheLastFiveSamples()
    {
        var detector = Detector(threshold: 8, windowSeconds: 100);
        Finding? finding = null;
        for (var i = 0; i < 8; i++) finding = detector.Observe(Request("1.1.1.1", i, path: $"/p{i}"));

        Assert.That(finding!.Samples, Has.Count.EqualTo(5));
        Assert.That(finding.Samples[^1], Does.Contain("/p7"));
    }

    [Test]
    public void IgnoresEntriesWithoutAnIp()
    {
        var detector = Detector(threshold: 1);

        Assert.That(detector.Observe(Request("1.1.1.1", 0) with { Ip = null }), Is.Null);
    }

    [Test]
    public void KeepsWorkingAcrossManyDistinctIps()
    {
        var detector = Detector(threshold: 2, windowSeconds: 1, cooldownSeconds: 1);

        for (var i = 0; i < 3500; i++)
            Assert.That(detector.Observe(Request($"10.0.{i / 250}.{i % 250}", i)), Is.Null);
    }

    [Test]
    public void WebScanner_FiresOnRepeatedProbePaths()
    {
        var detectors = DefaultDetectors.Create();
        var findings = new[] { "/.env", "/wp-login.php", "/phpmyadmin/index.php" }
            .Select((path, i) => Feed(detectors, Request("9.9.9.9", i, 404, path)))
            .SelectMany(found => found)
            .ToList();

        Assert.That(findings.Select(f => f.Rule), Does.Contain(DefaultDetectors.WebScanner));
    }

    [Test]
    public void WebScanner_IgnoresOrdinaryPaths()
    {
        var detectors = DefaultDetectors.Create();

        var findings = Enumerable.Range(0, 10).SelectMany(i => Feed(detectors, Request("9.9.9.9", i, 200, "/blog/post-" + i)));

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public void RequestFlood_FiresAboveTheRate()
    {
        var detectors = DefaultDetectors.Create(new DetectorOptions { FloodRequests = 50, FloodWindow = TimeSpan.FromSeconds(5) });

        var findings = Enumerable.Range(0, 60).SelectMany(i => Feed(detectors, Request("9.9.9.9", i * 0.05)));

        Assert.That(findings.Select(f => f.Rule), Does.Contain(DefaultDetectors.RequestFlood));
    }

    [Test]
    public void SshBruteForce_FiresOnRepeatedFailedLogins()
    {
        var detectors = DefaultDetectors.Create();

        var findings = Enumerable.Range(0, 5).SelectMany(i => Feed(detectors, FailedLogin("8.8.8.8", i))).ToList();

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.That(findings[0].Rule, Is.EqualTo(DefaultDetectors.SshBruteForce));
    }

    [Test]
    public void SshBruteForce_FiresOnConnectionsDroppedBeforeLoginOnAKeyOnlyServer()
    {
        var detectors = DefaultDetectors.Create();
        LogEntry Drop(double s) => new(LogSource.Auth, EntryKind.PreauthDrop, Start.AddSeconds(s), "8.8.8.8", $"drop {s}", User: "root");

        var findings = Enumerable.Range(0, 5).SelectMany(i => Feed(detectors, Drop(i))).ToList();

        Assert.That(findings.Select(f => f.Rule), Is.EqualTo(new[] { DefaultDetectors.SshBruteForce }));
    }

    [Test]
    public void SshBruteForce_DoesNotCountTheInvalidUserLineThatPrecedesEachDrop()
    {
        var detectors = DefaultDetectors.Create();
        LogEntry Invalid(double s) => new(LogSource.Auth, EntryKind.InvalidUser, Start.AddSeconds(s), "8.8.8.8", $"invalid {s}", User: "root");

        var findings = Enumerable.Range(0, 50).SelectMany(i => Feed(detectors, Invalid(i))).ToList();

        Assert.That(findings, Is.Empty);
    }

    private static IEnumerable<Finding> Feed(IReadOnlyList<IDetector> detectors, LogEntry entry) =>
        detectors.Select(d => d.Observe(entry)).OfType<Finding>();
}
