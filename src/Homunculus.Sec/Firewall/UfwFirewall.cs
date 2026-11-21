using System.Net;

namespace Homunculus.Sec.Firewall;

/// <summary>
/// Blocks addresses through a small root-owned wrapper around <c>ufw</c> (<c>deploy/homunculus-sec-ufw</c>), started with
/// <c>sudo -n</c> so it never prompts. The service's sudoers entry names only that wrapper, which validates the address again and
/// is the only place that talks to <c>ufw</c>. The address is parsed and re-formatted here before it goes anywhere near a command.
/// </summary>
public sealed class UfwFirewall(IProcessRunner runner, string helperPath = UfwFirewall.DefaultHelper) : IFirewall
{
    public const string DefaultHelper = "/usr/local/sbin/homunculus-sec-ufw";

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

    private static string Canonical(string ip) =>
        IPAddress.TryParse(ip, out var address) ? address.ToString() : throw new FirewallException($"'{ip}' is not an IP address.");
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
