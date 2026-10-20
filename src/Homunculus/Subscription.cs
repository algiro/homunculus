namespace Homunculus;

/// <summary>Runs an action once when disposed; used to unhook event handlers.</summary>
public sealed class Subscription(Action onDispose) : IDisposable
{
    public void Dispose() => onDispose();
}
