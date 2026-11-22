using System.Net;
using System.Net.Sockets;

namespace Homunculus.Sec.Safety;

/// <summary>
/// Addresses and networks in the one canonical text form the program uses everywhere: <c>203.0.113.9</c>, <c>203.0.113.0/24</c>,
/// <c>2001:db8:1:2::/64</c>. A network must have no host bits set, and may not be wider than a /24 (IPv4) or a /48 (IPv6): blocking
/// a bigger range would shut out far too many innocent visitors.
/// </summary>
public static class Cidr
{
    public const int WidestPrefixV4 = 24;
    public const int WidestPrefixV6 = 48;

    /// <summary>The prefix length of the networks that group attackers: a /24 for IPv4, a /64 for IPv6.</summary>
    public const int GroupPrefixV4 = 24;
    public const int GroupPrefixV6 = 64;

    public static bool IsNetwork(string text) => text.Contains('/', StringComparison.Ordinal);

    /// <summary>Parses an address or a network; a single address is a /32 or /128. Fails for non-canonical text.</summary>
    public static bool TryParse(string text, out IPNetwork network)
    {
        network = default;
        if (IsNetwork(text)) return IPNetwork.TryParse(text, out network) && network.ToString() == text && !IsSingleAddress(network);
        if (!IPAddress.TryParse(text, out var address) || address.ToString() != text) return false;
        network = new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        return true;
    }

    /// <summary>The canonical text of an address or network, or null when it is not one (including host bits set, or a range that is too wide).</summary>
    public static string? Canonical(string text)
    {
        if (IsNetwork(text))
        {
            // IPNetwork.TryParse quietly masks host bits (8.8.8.5/24 becomes 8.8.8.0/24); a request with host bits set is a mistake, not a network.
            if (!IPNetwork.TryParse(text, out var network) || network.ToString() != text || !IsNarrowEnough(network) || IsSingleAddress(network)) return null;
            return network.ToString();
        }

        return IPAddress.TryParse(text, out var address) ? address.ToString() : null;
    }

    /// <summary>A /32 or /128: one address, which has its own canonical form without the prefix.</summary>
    private static bool IsSingleAddress(IPNetwork network) =>
        network.PrefixLength == (network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);

    public static bool IsNarrowEnough(IPNetwork network) =>
        network.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? network.PrefixLength >= WidestPrefixV4 : network.PrefixLength >= WidestPrefixV6;

    /// <summary>True when <paramref name="outer"/> (an address or network) contains all of <paramref name="inner"/>.</summary>
    public static bool Covers(string outer, string inner) =>
        TryParse(outer, out var o) && TryParse(inner, out var i) && Covers(o, i);

    public static bool Covers(IPNetwork outer, IPNetwork inner) =>
        outer.BaseAddress.AddressFamily == inner.BaseAddress.AddressFamily && outer.PrefixLength <= inner.PrefixLength && outer.Contains(inner.BaseAddress);

    /// <summary>True when the two share any address: for networks that is the same as one containing the other's first address.</summary>
    public static bool Overlaps(IPNetwork a, IPNetwork b) =>
        a.BaseAddress.AddressFamily == b.BaseAddress.AddressFamily && (a.Contains(b.BaseAddress) || b.Contains(a.BaseAddress));

    /// <summary>The /24 (IPv4) or /64 (IPv6) network an address belongs to, in canonical text. Null for something that is not an address.</summary>
    public static string? GroupOf(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var prefix = address.AddressFamily == AddressFamily.InterNetwork ? GroupPrefixV4 : GroupPrefixV6;
        var bytes = address.GetAddressBytes();
        for (var bit = prefix; bit < bytes.Length * 8; bit++) bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        return new IPNetwork(new IPAddress(bytes), prefix).ToString();
    }
}
