namespace Homunculus.Logging;

/// <summary>A structured debug log: one named event with its data per call. Not for the user, who has <see cref="IUserInterface"/>.</summary>
public interface IEventLog
{
    void Write(string eventName, object? data = null);
}

/// <summary>The default: logging is opt-in, so nothing is recorded unless a file is configured.</summary>
public sealed class NullEventLog : IEventLog
{
    public static readonly NullEventLog Instance = new();

    public void Write(string eventName, object? data = null)
    {
    }
}
