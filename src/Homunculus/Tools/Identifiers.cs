using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>The names (words made of letters, digits and underscores) a piece of text uses.</summary>
public static partial class Identifiers
{
    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Name();

    public static HashSet<string> In(string text) => Name().Matches(text).Select(match => match.Value).ToHashSet();
}
