using System.Text.RegularExpressions;

namespace Homunculus.Sec.Detection;

/// <summary>What in a web request looks like scanning or attacking. Plain pattern checks; the model is not involved.</summary>
public static partial class ScannerPaths
{
    /// <summary>Paths that only scanners and exploit kits ask for on a typical site. One signal among several, not a verdict on its own.</summary>
    [GeneratedRegex(@"(?:^|/)(?:\.env|\.git/|\.aws/|\.ssh/|wp-login\.php|xmlrpc\.php|wp-admin|phpmyadmin|pma/|\.DS_Store|cgi-bin/|boaform|actuator/|vendor/phpunit|etc/passwd|\.\./)", RegexOptions.IgnoreCase)]
    private static partial Regex Probe();

    /// <summary>Traversal and injection strings that no ordinary visitor sends.</summary>
    [GeneratedRegex(@"(\.\./|%2e%2e|%00|union(\s|%20|\+)select|<script|\$\{jndi|/bin/(ba)?sh|eval\(|base64_decode|;wget|;curl)", RegexOptions.IgnoreCase)]
    private static partial Regex Injection();

    /// <summary>Tools that announce themselves. Weak evidence: legitimate clients use some of these too.</summary>
    [GeneratedRegex(@"sqlmap|nikto|nmap|masscan|zgrab|gobuster|dirbuster|wpscan|nuclei|fuzz|python-requests|go-http-client|libwww", RegexOptions.IgnoreCase)]
    private static partial Regex Agent();

    private static readonly HashSet<string> Standard = new(StringComparer.Ordinal) { "GET", "POST", "HEAD", "OPTIONS", "PUT", "DELETE", "PATCH" };

    /// <summary>Files that browsers, crawlers and link checkers ask for whether or not a site has them: a miss is not a sign of anything.</summary>
    private static readonly HashSet<string> Expected = new(StringComparer.Ordinal)
    {
        "/", "/favicon.ico", "/robots.txt", "/sitemap.xml", "/ads.txt", "/humans.txt", "/apple-touch-icon.png", "/apple-touch-icon-precomposed.png",
    };

    public static bool IsProbe(string path) => Probe().IsMatch(path);

    public static bool LooksLikeInjection(string path) => Injection().IsMatch(path);

    public static bool IsScannerAgent(string? userAgent) => userAgent is not null && Agent().IsMatch(userAgent);

    public static bool IsStandardMethod(string method) => Standard.Contains(method);

    /// <summary>A path without its query string, lower case, and short enough to keep as a key.</summary>
    public static string Normalize(string path)
    {
        var cut = path.IndexOf('?', StringComparison.Ordinal);
        var bare = (cut >= 0 ? path[..cut] : path).ToLowerInvariant();
        return bare.Length > 80 ? bare[..80] : bare;
    }

    /// <summary>True for the usual files a missing 404 on which says nothing about the visitor.</summary>
    public static bool IsExpectedMiss(string normalizedPath) => Expected.Contains(normalizedPath);
}
