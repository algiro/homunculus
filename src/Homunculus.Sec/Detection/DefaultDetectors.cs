using System.Text.RegularExpressions;
using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Detection;

public static partial class DefaultDetectors
{
    public const string RequestFlood = "request_flood";
    public const string NotFoundBurst = "not_found_burst";
    public const string AuthDeniedBurst = "auth_denied_burst";
    public const string ScannerProbe = "scanner_probe";
    public const string SshBruteForce = "ssh_brute_force";
    public const string RateLimited = "rate_limited";

    /// <summary>Paths that only scanners and exploit kits ask for on a typical site.</summary>
    [GeneratedRegex(@"(?:^|/)(?:\.env|\.git/|\.aws/|\.ssh/|wp-login\.php|xmlrpc\.php|wp-admin|phpmyadmin|pma/|\.DS_Store|cgi-bin/|boaform|actuator/|vendor/phpunit|etc/passwd|\.\./)", RegexOptions.IgnoreCase)]
    private static partial Regex ProbePath();

    public static IReadOnlyList<IDetector> Create(DetectorOptions? options = null)
    {
        var o = options ?? new DetectorOptions();
        return
        [
            new WindowDetector(RequestFlood, e => e.Kind == EntryKind.Request, o.FloodRequests, o.FloodWindow, o.Cooldown),
            new WindowDetector(NotFoundBurst, e => e is { Kind: EntryKind.Request, Status: 404 }, o.NotFoundCount, o.NotFoundWindow, o.Cooldown),
            new WindowDetector(AuthDeniedBurst, e => e is { Kind: EntryKind.Request, Status: 401 or 403 }, o.AuthDeniedCount, o.AuthDeniedWindow, o.Cooldown),
            new WindowDetector(ScannerProbe, e => e is { Kind: EntryKind.Request, Path: { } path } && ProbePath().IsMatch(path), o.ProbeCount, o.ProbeWindow, o.Cooldown),
            new WindowDetector(SshBruteForce, e => e.Kind == EntryKind.FailedLogin, o.SshFailureCount, o.SshFailureWindow, o.Cooldown),
            new WindowDetector(RateLimited, e => e.Kind == EntryKind.RateLimited, o.RateLimitedCount, o.RateLimitedWindow, o.Cooldown),
        ];
    }
}
