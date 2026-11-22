using System.Net;

namespace Homunculus.Sec.Safety;

/// <summary>
/// Addresses that must never be blocked: loopback, private and link-local ranges, plus whatever the operator lists (their own
/// IP, the office, a monitoring service). Entries are single addresses or CIDR ranges.
/// </summary>
public sealed class Allowlist
{
    private static readonly string[] BuiltIn =
    [
        "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16", "100.64.0.0/10", "0.0.0.0/8",
        "::1/128", "fc00::/7", "fe80::/10", "::/128",
    ];

    private readonly List<IPNetwork> _networks;

    /// <exception cref="FormatException">An entry is not an IP address or a CIDR range.</exception>
    public Allowlist(IEnumerable<string>? extra = null)
    {
        _networks = BuiltIn.Concat(extra ?? []).Select(Parse).ToList();
    }

    /// <summary>
    /// True when the target is protected, or when it is not a valid address or network at all (anything unparseable is never acted on).
    /// A network is protected when it shares any address with a protected one, and when it is wider than blocking allows (a /24, or a /48
    /// for IPv6).
    /// </summary>
    public bool IsProtected(string target)
    {
        if (Cidr.IsNetwork(target))
            return !Cidr.TryParse(target, out var network) || !Cidr.IsNarrowEnough(network) || _networks.Any(protectedNetwork => Cidr.Overlaps(protectedNetwork, network));

        var ip = target;
        if (!IPAddress.TryParse(ip, out var address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return _networks.Any(network => network.Contains(address));
    }

    private static IPNetwork Parse(string entry)
    {
        var text = entry.Trim();
        if (IPNetwork.TryParse(text, out var network)) return network;
        if (IPAddress.TryParse(text, out var single))
            return new IPNetwork(single, single.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128);
        throw new FormatException($"'{entry}' is not an IP address or a CIDR range.");
    }
}
