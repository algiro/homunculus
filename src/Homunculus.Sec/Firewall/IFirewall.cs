namespace Homunculus.Sec.Firewall;

/// <summary>The firewall command failed or could not be run.</summary>
public sealed class FirewallException(string message) : Exception(message);

public interface IFirewall
{
    /// <exception cref="FirewallException">The address could not be blocked.</exception>
    Task BlockAsync(string ip, CancellationToken cancellation);

    /// <exception cref="FirewallException">The block could not be removed.</exception>
    Task UnblockAsync(string ip, CancellationToken cancellation);
}
