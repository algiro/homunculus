namespace Homunculus.Sec.Detection;

/// <summary>Thresholds for the built-in rules. The defaults suit a small public web server.</summary>
public sealed record DetectorOptions
{
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(10);

    public int FloodRequests { get; init; } = 150;
    public TimeSpan FloodWindow { get; init; } = TimeSpan.FromSeconds(10);

    public int NotFoundCount { get; init; } = 30;
    public TimeSpan NotFoundWindow { get; init; } = TimeSpan.FromMinutes(1);

    public int AuthDeniedCount { get; init; } = 20;
    public TimeSpan AuthDeniedWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>The web scanner score (see <see cref="WebScannerDetector"/>): points needed, how long points last, and how long an IP stays quiet after an alert.</summary>
    public int WebScoreThreshold { get; init; } = 10;
    public TimeSpan WebScoreWindow { get; init; } = TimeSpan.FromHours(1);
    public TimeSpan WebScoreCooldown { get; init; } = TimeSpan.FromHours(1);

    public int SshFailureCount { get; init; } = 5;
    public TimeSpan SshFailureWindow { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Slow guessing: many attempts from one address spread over a day, far below the per-minute limit.</summary>
    public int SshPersistentCount { get; init; } = 50;
    public TimeSpan SshPersistentWindow { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan SshPersistentCooldown { get; init; } = TimeSpan.FromHours(24);

    /// <summary>One network (a /24, or a /64 for IPv6) attacking with several addresses, each too quietly to be noticed alone.</summary>
    public int SubnetMinAddresses { get; init; } = 3;
    public int SubnetMinEvents { get; init; } = 50;
    public int SubnetMinEventsPerAddress { get; init; } = 3;
    public TimeSpan SubnetWindow { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan SubnetCooldown { get; init; } = TimeSpan.FromHours(24);

    public int RateLimitedCount { get; init; } = 10;
    public TimeSpan RateLimitedWindow { get; init; } = TimeSpan.FromMinutes(1);
}
