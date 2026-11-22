using Homunculus.Sec.Analysis;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Logs;
using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Tests.Detection;

public class NetworkDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

    private static NetworkDetector Detector(int addresses = 3, int events = 50, int hours = 24, int cooldownHours = 24) =>
        new("ssh_subnet", e => e.Kind is EntryKind.FailedLogin or EntryKind.PreauthDrop, addresses, events, TimeSpan.FromHours(hours), TimeSpan.FromHours(cooldownHours));

    private static LogEntry Drop(string ip, double minutes) =>
        new(LogSource.Auth, EntryKind.PreauthDrop, Start.AddMinutes(minutes), ip, $"drop {ip}", User: "root");

    private static LogEntry Login(string ip, double minutes) =>
        new(LogSource.Auth, EntryKind.LoginSuccess, Start.AddMinutes(minutes), ip, $"login {ip}", User: "deploy");

    private static Finding? Feed(NetworkDetector detector, IEnumerable<LogEntry> entries)
    {
        Finding? found = null;
        foreach (var entry in entries) found ??= detector.Observe(entry);
        return found;
    }

    /// <summary>Each of the addresses sends a few attempts an hour: far too slow for any single-address rule.</summary>
    private static IEnumerable<LogEntry> Slow(string prefix, int addresses, int attemptsEach) =>
        Enumerable.Range(0, attemptsEach).SelectMany(round => Enumerable.Range(0, addresses).Select(a => Drop($"{prefix}.{10 + a}", round * 30 + a)));

    [Test]
    public void ReportsTheNetworkWhenSeveralQuietAddressesTogetherAttackEnough()
    {
        var finding = Feed(Detector(), Slow("203.0.113", addresses: 4, attemptsEach: 14))!;

        Assert.That(finding.Rule, Is.EqualTo("ssh_subnet"));
        Assert.That(finding.Ip, Is.EqualTo("203.0.113.0/24"));
        Assert.That(finding.Summary, Does.Contain("4 different addresses").And.Contain("Busiest addresses"));
        Assert.That(finding.Caution, Is.Null);
    }

    [Test]
    public void OneNoisyAddressIsNotANetworkAttack()
    {
        var entries = Enumerable.Range(0, 300).Select(i => Drop("203.0.113.9", i));

        Assert.That(Feed(Detector(), entries), Is.Null);
    }

    [Test]
    public void BystandersWithOneOrTwoAttemptsDoNotCountAsMembersOfTheAttack()
    {
        // Two loud addresses and four that tried once each: that is two attackers, not a network of six.
        var entries = Enumerable.Range(0, 80).Select(i => Drop(i % 2 == 0 ? "203.0.113.10" : "203.0.113.11", i))
            .Concat(Enumerable.Range(20, 4).Select(i => Drop($"203.0.113.{i}", 90 + i)));

        Assert.That(Feed(Detector(), entries), Is.Null);
    }

    [Test]
    public void TwoAddressesAreNotEnough()
    {
        Assert.That(Feed(Detector(), Slow("203.0.113", addresses: 2, attemptsEach: 60)), Is.Null);
    }

    [Test]
    public void ManyAddressesWithTooFewAttemptsAreNotEnough()
    {
        Assert.That(Feed(Detector(), Slow("203.0.113", addresses: 6, attemptsEach: 3)), Is.Null);
    }

    [Test]
    public void AddressesOfDifferentNetworksAreNotCombined()
    {
        var entries = Enumerable.Range(0, 60).Select(i => Drop($"10.{i % 20}.{i % 7}.1", i));

        Assert.That(Feed(Detector(), entries), Is.Null);
    }

    [Test]
    public void AttemptsOlderThanTheWindowStopCounting()
    {
        var detector = Detector();
        Assert.That(Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 11)), Is.Null);   // 44 attempts, not yet 50

        // Two days later the old ones are gone, so a few more do not add up.
        var later = Enumerable.Range(0, 5).Select(i => Drop($"203.0.113.{10 + i}", 60 * 48 + i));
        Assert.That(Feed(detector, later), Is.Null);
    }

    [Test]
    public void ASuccessfulLoginFromTheNetworkIsReportedAsACaution()
    {
        var detector = Detector();
        detector.Observe(Login("203.0.113.200", 5));

        var finding = Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 14))!;

        Assert.That(finding.Caution, Does.Contain("logged in successfully").And.Contain("cut them off"));
    }

    [Test]
    public void ALoginFromAnotherNetworkIsNoCaution()
    {
        var detector = Detector();
        detector.Observe(Login("198.51.100.200", 5));

        Assert.That(Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 14))!.Caution, Is.Null);
    }

    [Test]
    public void ItReportsAgainOnlyAfterTheCooldown()
    {
        var detector = Detector(cooldownHours: 24);
        Assert.That(Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 14)), Is.Not.Null);

        Assert.That(Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 2).Select(e => e with { Time = e.Time.AddHours(5) })), Is.Null);
        Assert.That(Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 14).Select(e => e with { Time = e.Time.AddHours(30) })), Is.Not.Null);
    }

    [Test]
    public void ItGroupsIpv6AddressesByTheirSlash64()
    {
        var entries = Enumerable.Range(0, 60).Select(i => Drop($"2606:4700:4700:1::{i % 4 + 1:x}", i));

        var finding = Feed(Detector(), entries)!;

        Assert.That(finding.Ip, Is.EqualTo("2606:4700:4700:1::/64"));
    }

    [Test]
    public void TheDefaultRulesIncludeTheSubnetAndThePersistentRule()
    {
        var detectors = DefaultDetectors.Create();
        var entries = Slow("203.0.113", addresses: 4, attemptsEach: 14).ToList();

        var findings = entries.SelectMany(e => detectors.Select(d => d.Observe(e)).OfType<Finding>()).ToList();

        Assert.That(findings.Select(f => f.Rule), Does.Contain(DefaultDetectors.SshSubnet));
        Assert.That(findings.Select(f => f.Rule), Does.Not.Contain(DefaultDetectors.SshBruteForce));
    }

    [Test]
    public void APersistentSlowGuesserIsCaughtByTheDailyRule()
    {
        var detectors = DefaultDetectors.Create();
        // 60 attempts over 20 hours from one address: three an hour, never close to five in a minute.
        var entries = Enumerable.Range(0, 60).Select(i => Drop("8.8.4.4", i * 20.0));

        var findings = entries.SelectMany(e => detectors.Select(d => d.Observe(e)).OfType<Finding>()).ToList();

        Assert.That(findings.Select(f => f.Rule), Is.EqualTo(new[] { DefaultDetectors.SshPersistent }));
    }

    [Test]
    public void ANetworkProposalIsKeptShortAndRefusedWhenItTouchesAProtectedAddress()
    {
        var finding = Feed(Detector(), Slow("203.0.113", addresses: 4, attemptsEach: 14))!;
        var proposal = new Proposal(finding, Severity.High, ActionKind.BlockIp, TimeSpan.FromDays(7), "s", "r");

        var plain = new ProposalGuard(new Allowlist()).Apply(proposal);
        var guarded = new ProposalGuard(new Allowlist(["203.0.113.77"])).Apply(proposal);

        Assert.That(plain.BlockFor, Is.EqualTo(TimeSpan.FromHours(48)));
        Assert.That(guarded.Action, Is.EqualTo(ActionKind.Ignore));
    }

    [Test]
    public void TheModelSeesTheNetworkTheSummaryAndTheCaution()
    {
        var detector = Detector();
        detector.Observe(Login("203.0.113.200", 5));
        var finding = Feed(detector, Slow("203.0.113", addresses: 4, attemptsEach: 14))!;

        var text = Analyst.Describe(finding);

        Assert.That(text, Does.Contain("Network: 203.0.113.0/24").And.Contain("Caution from the monitor").And.Contain("Busiest addresses"));
    }
}
