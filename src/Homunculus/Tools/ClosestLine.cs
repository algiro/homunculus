using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>
/// Finds the line of a file that a model most likely meant when the text it quoted is not there, so it can copy the real line.
/// Lines are ranked by the identifiers they share with the quoted text; an identifier found on few lines counts for more.
/// </summary>
public static partial class ClosestLine
{
    private const int MaxChars = 200;

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();

    public static (int Number, string Text)? Find(string content, string quoted)
    {
        var wanted = quoted.Split('\n').Select(Identifiers).FirstOrDefault(identifiers => identifiers.Count > 0);
        if (wanted is null) return null;

        var lines = content.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var identifiersOfLine = lines.Select(Identifiers).ToArray();
        var scores = new double[lines.Length];
        foreach (var identifier in wanted)
        {
            var holders = identifiersOfLine.Count(found => found.Contains(identifier));
            for (var i = 0; i < lines.Length; i++)
                if (identifiersOfLine[i].Contains(identifier)) scores[i] += 1.0 / holders;
        }

        var best = Array.IndexOf(scores, scores.Max());
        return scores[best] > 0 ? (best + 1, Shorten(lines[best].Trim())) : null;
    }

    private static HashSet<string> Identifiers(string text) =>
        Identifier().Matches(text).Select(match => match.Value).ToHashSet();

    private static string Shorten(string line) => line.Length <= MaxChars ? line : line[..MaxChars] + "...";
}
