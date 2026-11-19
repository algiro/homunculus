using System.Net;

namespace Homunculus.Sec.Firewall;

/// <summary>
/// Blocks addresses with <c>ufw</c>, through <c>sudo -n</c> (never prompting) so the service can run as an ordinary user whose
/// sudoers entry allows only these two commands. The address is parsed and re-formatted before it goes anywhere near a command.
/// </summary>
public sealed class UfwFirewall(IProcessRunner runner, bool useSudo = true) : IFirewall
{
    public const string RuleComment = "homunculus-sec";

    public async Task BlockAsync(string ip, CancellationToken cancellation)
    {
        var address = Canonical(ip);
        // Rule 1 so the deny comes before any broad allow; an empty rule set has no position 1, so fall back to a plain deny.
        var result = await RunUfwAsync(["insert", "1", "deny", "from", address, "comment", RuleComment], cancellation);
        if (result.ExitCode != 0 && result.Output.Contains("Invalid position", StringComparison.OrdinalIgnoreCase))
            result = await RunUfwAsync(["deny", "from", address, "comment", RuleComment], cancellation);

        if (result.ExitCode != 0) throw new FirewallException($"ufw could not block {address}: {result.Output}");
    }

    public async Task UnblockAsync(string ip, CancellationToken cancellation)
    {
        var address = Canonical(ip);
        var result = await RunUfwAsync(["delete", "deny", "from", address], cancellation);
        if (result.ExitCode != 0 && !result.Output.Contains("non-existent", StringComparison.OrdinalIgnoreCase))
            throw new FirewallException($"ufw could not unblock {address}: {result.Output}");
    }

    private Task<ProcessResult> RunUfwAsync(string[] arguments, CancellationToken cancellation) =>
        useSudo
            ? runner.RunAsync("sudo", ["-n", "ufw", .. arguments], cancellation)
            : runner.RunAsync("ufw", arguments, cancellation);

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
