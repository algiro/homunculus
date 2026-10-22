namespace Homunculus.Tests.Support;

/// <summary>A clock the test moves by hand, so no test ever sleeps.</summary>
public sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
