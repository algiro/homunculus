using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Tests.Safety;

public class CidrTests
{
    [TestCase("8.8.8.8", "8.8.8.8")]
    [TestCase("8.8.8.0/24", "8.8.8.0/24")]
    [TestCase("8.8.8.0/25", "8.8.8.0/25")]
    [TestCase("2606:4700:4700::/48", "2606:4700:4700::/48")]
    [TestCase("2606:4700:4700::1111", "2606:4700:4700::1111")]
    public void CanonicalAcceptsAddressesAndNarrowNetworks(string text, string expected) =>
        Assert.That(Cidr.Canonical(text), Is.EqualTo(expected));

    [TestCase("8.8.8.0/23")]
    [TestCase("8.0.0.0/8")]
    [TestCase("0.0.0.0/0")]
    [TestCase("::/0")]
    [TestCase("2606:4700::/32")]
    [TestCase("8.8.8.5/24")]
    [TestCase("8.8.8.0/33")]
    [TestCase("8.8.8.8/32")]
    [TestCase("not-an-ip/24")]
    [TestCase("8.8.8.0/24 ")]
    public void CanonicalRefusesWideHostBitOrMalformedNetworks(string text) =>
        Assert.That(Cidr.Canonical(text), Is.Null);

    [TestCase("203.0.113.77", "203.0.113.0/24")]
    [TestCase("8.8.8.8", "8.8.8.0/24")]
    [TestCase("2606:4700:4700::1111", "2606:4700:4700::/64")]
    [TestCase("::ffff:8.8.8.8", "8.8.8.0/24")]
    public void GroupOfGivesTheNetworkAnAddressBelongsTo(string ip, string expected) =>
        Assert.That(Cidr.GroupOf(ip), Is.EqualTo(expected));

    [Test]
    public void GroupOfIsNullForNonsense() => Assert.That(Cidr.GroupOf("banana"), Is.Null);

    [TestCase("8.8.8.0/24", "8.8.8.8", true)]
    [TestCase("8.8.8.0/24", "8.8.9.8", false)]
    [TestCase("8.8.8.0/24", "8.8.8.0/25", true)]
    [TestCase("8.8.8.0/25", "8.8.8.0/24", false)]
    [TestCase("8.8.8.8", "8.8.8.8", true)]
    [TestCase("8.8.8.8", "8.8.8.9", false)]
    [TestCase("8.8.8.0/24", "2606:4700::1", false)]
    public void CoversMeansEveryAddressOfTheInnerIsInTheOuter(string outer, string inner, bool expected) =>
        Assert.That(Cidr.Covers(outer, inner), Is.EqualTo(expected));
}

public class AllowlistNetworkTests
{
    [TestCase("10.1.2.0/24")]
    [TestCase("192.168.0.0/24")]
    [TestCase("127.0.0.0/24")]
    [TestCase("100.64.1.0/24")]
    [TestCase("fe80::/64")]
    public void PrivateNetworksAreProtected(string network) =>
        Assert.That(new Allowlist().IsProtected(network), Is.True);

    [Test]
    public void ANetworkThatContainsAProtectedAddressIsProtected()
    {
        var allowlist = new Allowlist(["203.0.113.77", "198.51.100.0/25"]);

        Assert.That(allowlist.IsProtected("203.0.113.0/24"), Is.True);
        Assert.That(allowlist.IsProtected("198.51.100.0/24"), Is.True);
        Assert.That(allowlist.IsProtected("198.51.101.0/24"), Is.False);
    }

    [TestCase("8.8.8.0/23")]
    [TestCase("8.0.0.0/8")]
    [TestCase("8.8.8.5/24")]
    [TestCase("2606:4700::/32")]
    [TestCase("nonsense/24")]
    public void ANetworkThatIsTooWideOrMalformedIsNeverBlocked(string network) =>
        Assert.That(new Allowlist().IsProtected(network), Is.True);

    [Test]
    public void APublicNarrowNetworkIsAllowed() => Assert.That(new Allowlist().IsProtected("8.8.8.0/24"), Is.False);
}
