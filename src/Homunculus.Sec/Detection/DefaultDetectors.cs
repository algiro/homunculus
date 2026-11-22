using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Detection;

public static class DefaultDetectors
{
    public const string RequestFlood = "request_flood";
    public const string NotFoundBurst = "not_found_burst";
    public const string AuthDeniedBurst = "auth_denied_burst";
    public const string WebScanner = "web_scanner";
    public const string SshBruteForce = "ssh_brute_force";
    public const string SshPersistent = "ssh_persistent";
    public const string SshSubnet = "ssh_subnet";
    public const string RateLimited = "rate_limited";

    private static bool IsSshFailure(LogEntry e) => e.Kind is EntryKind.FailedLogin or EntryKind.PreauthDrop;

    public static IReadOnlyList<IDetector> Create(DetectorOptions? options = null)
    {
        var o = options ?? new DetectorOptions();
        return
        [
            new WindowDetector(RequestFlood, e => e.Kind == EntryKind.Request, o.FloodRequests, o.FloodWindow, o.Cooldown),
            new WindowDetector(NotFoundBurst, e => e is { Kind: EntryKind.Request, Status: 404 }, o.NotFoundCount, o.NotFoundWindow, o.Cooldown),
            new WindowDetector(AuthDeniedBurst, e => e is { Kind: EntryKind.Request, Status: 401 or 403 }, o.AuthDeniedCount, o.AuthDeniedWindow, o.Cooldown),
            new WebScannerDetector(WebScanner, o.WebScoreThreshold, o.WebScoreWindow, o.WebScoreCooldown),
            new WindowDetector(SshBruteForce, IsSshFailure, o.SshFailureCount, o.SshFailureWindow, o.Cooldown),
            new WindowDetector(SshPersistent, IsSshFailure, o.SshPersistentCount, o.SshPersistentWindow, o.SshPersistentCooldown),
            new NetworkDetector(SshSubnet, IsSshFailure, o.SubnetMinAddresses, o.SubnetMinEvents, o.SubnetWindow, o.SubnetCooldown, o.SubnetMinEventsPerAddress),
            new WindowDetector(RateLimited, e => e.Kind == EntryKind.RateLimited, o.RateLimitedCount, o.RateLimitedWindow, o.Cooldown),
        ];
    }
}
