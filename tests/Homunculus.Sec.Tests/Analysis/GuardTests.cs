using Homunculus.Sec.Analysis;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Tests.Analysis;

public class GuardTests
{
    private static Proposal BlockProposal(string ip, TimeSpan? hours = null) => new(
        new Finding("rule", ip, DateTimeOffset.UnixEpoch, 1, TimeSpan.FromSeconds(1), []),
        Severity.High, ActionKind.BlockIp, hours ?? TimeSpan.FromHours(2), "s", "r");

    [TestCase("127.0.0.1")]
    [TestCase("10.1.2.3")]
    [TestCase("192.168.0.20")]
    [TestCase("172.20.0.1")]
    [TestCase("::1")]
    [TestCase("fe80::1")]
    [TestCase("::ffff:192.168.1.1")]
    [TestCase("not-an-ip")]
    public void NeverBlocksProtectedOrInvalidAddresses(string ip)
    {
        var guarded = new ProposalGuard(new Allowlist()).Apply(BlockProposal(ip));

        Assert.That(guarded.Action, Is.EqualTo(ActionKind.Ignore));
        Assert.That(guarded.Reason, Does.Contain("never-block"));
    }

    [Test]
    public void LetsAPublicAddressThrough()
    {
        var guarded = new ProposalGuard(new Allowlist()).Apply(BlockProposal("203.0.113.5"));

        Assert.That(guarded.Action, Is.EqualTo(ActionKind.BlockIp));
    }

    [Test]
    public void HonoursTheOperatorsListOfAddressesAndRanges()
    {
        var guard = new ProposalGuard(new Allowlist(["203.0.113.5", "198.51.100.0/24"]));

        Assert.That(guard.Apply(BlockProposal("203.0.113.5")).Action, Is.EqualTo(ActionKind.Ignore));
        Assert.That(guard.Apply(BlockProposal("198.51.100.77")).Action, Is.EqualTo(ActionKind.Ignore));
        Assert.That(guard.Apply(BlockProposal("203.0.113.6")).Action, Is.EqualTo(ActionKind.BlockIp));
    }

    [Test]
    public void RejectsAMalformedAllowlistEntry() =>
        Assert.Throws<FormatException>(() => new Allowlist(["banana"]));

    [Test]
    public void CapsTheBlockDurationAndFixesNonPositiveOnes()
    {
        var guard = new ProposalGuard(new Allowlist(), maxBlock: TimeSpan.FromDays(3));

        Assert.That(guard.Apply(BlockProposal("203.0.113.5", TimeSpan.FromDays(30))).BlockFor, Is.EqualTo(TimeSpan.FromDays(3)));
        Assert.That(guard.Apply(BlockProposal("203.0.113.5", TimeSpan.Zero)).BlockFor, Is.EqualTo(TimeSpan.FromHours(24)));
    }
}
