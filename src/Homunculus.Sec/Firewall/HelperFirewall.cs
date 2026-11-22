using Homunculus.Sec.Safety;

namespace Homunculus.Sec.Firewall;

/// <summary>
/// Blocks addresses through a small root-owned wrapper script, started with <c>sudo -n</c> so it never prompts. The service's
/// sudoers entry names only that wrapper, which validates the address again and is the only place that talks to the firewall.
/// The address is parsed and re-formatted here before it goes anywhere near a command.
/// </summary>
public abstract class HelperFirewall(IProcessRunner runner, string helperPath) : IFirewall
{
    public Task BlockAsync(string ip, CancellationToken cancellation) => RunAsync("block", ip, cancellation);

    public Task UnblockAsync(string ip, CancellationToken cancellation) => RunAsync("unblock", ip, cancellation);

    private async Task RunAsync(string action, string ip, CancellationToken cancellation)
    {
        var address = Canonical(ip);
        var result = await runner.RunAsync("sudo", ["-n", helperPath, action, address], cancellation);

        // Removing a rule that is already gone is the outcome that was wanted.
        if (result.ExitCode != 0 && !(action == "unblock" && result.Output.Contains("non-existent", StringComparison.OrdinalIgnoreCase)))
            throw new FirewallException($"Could not {action} {address}: {result.Output}");
    }

    private static string Canonical(string target) =>
        Cidr.Canonical(target) ?? throw new FirewallException($"'{target}' is not an IP address or a network of at most /{Cidr.WidestPrefixV4} (IPv4) or /{Cidr.WidestPrefixV6} (IPv6) size.");
}

/// <summary>
/// Blocks with iptables (<c>deploy/homunculus-sec-fw</c>): a DROP rule for the address in INPUT, for the host itself, and in
/// Docker's DOCKER-USER chain, for published container ports, which <c>ufw</c> rules do not reach. It changes no policy and needs
/// no <c>ufw</c>.
/// </summary>
public sealed class IptablesFirewall(IProcessRunner runner, string helperPath = IptablesFirewall.DefaultHelper) : HelperFirewall(runner, helperPath)
{
    public const string DefaultHelper = "/usr/local/sbin/homunculus-sec-fw";
}

/// <summary>Blocks with <c>ufw deny from</c> (<c>deploy/homunculus-sec-ufw</c>). Needs ufw to be enabled, and does not cover Docker-published ports.</summary>
public sealed class UfwFirewall(IProcessRunner runner, string helperPath = UfwFirewall.DefaultHelper) : HelperFirewall(runner, helperPath)
{
    public const string DefaultHelper = "/usr/local/sbin/homunculus-sec-ufw";
}

/// <summary>Does nothing but record what it would have done. For trying the service out before giving it any rights.</summary>
public sealed class DryRunFirewall : IFirewall
{
    private readonly List<string> _actions = [];

    public IReadOnlyList<string> Actions => _actions;

    public Task BlockAsync(string ip, CancellationToken cancellation)
    {
        _actions.Add($"block {ip}");
        return Task.CompletedTask;
    }

    public Task UnblockAsync(string ip, CancellationToken cancellation)
    {
        _actions.Add($"unblock {ip}");
        return Task.CompletedTask;
    }
}
