using System.Text;
using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>
/// A file pattern such as "src/**/*.cs" or "*.{cs,csproj}", matched against paths written with forward slashes.
/// "**" crosses folders, "*" and "?" stay inside one, "{a,b}" is either. Case does not matter. A pattern without a "/"
/// matches in any folder, so "*.cs" finds every C# file.
/// </summary>
public sealed class GlobPattern
{
    private readonly Regex _regex;

    public GlobPattern(string pattern)
    {
        var normalized = pattern.Trim().Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized[2..];
        normalized = normalized.TrimStart('/');
        if (!normalized.Contains('/')) normalized = "**/" + normalized;

        _regex = new Regex("^" + ToRegex(normalized) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    public bool IsMatch(string relativePath) => _regex.IsMatch(relativePath);

    private static string ToRegex(string pattern)
    {
        var regex = new StringBuilder();
        var braces = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '*' when i + 1 < pattern.Length && pattern[i + 1] == '*':
                    var slashFollows = i + 2 < pattern.Length && pattern[i + 2] == '/';
                    regex.Append(slashFollows ? "(?:.*/)?" : ".*");
                    i += slashFollows ? 2 : 1;
                    break;
                case '*': regex.Append("[^/]*"); break;
                case '?': regex.Append("[^/]"); break;
                case '{': braces++; regex.Append("(?:"); break;
                case '}' when braces > 0: braces--; regex.Append(')'); break;
                case ',' when braces > 0: regex.Append('|'); break;
                default: regex.Append(Regex.Escape(pattern[i].ToString())); break;
            }
        }

        return regex.Append(')', braces).ToString();
    }
}
