using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>
/// Explains why text a model quoted from a file does not match it: which of its lines is not in the file, and which real line
/// it most likely meant, so the model can copy that line. Lines are ranked by the identifiers they share with the quoted line;
/// an identifier found on few lines counts for more.
/// </summary>
public static partial class ClosestLine
{
    private const int MaxChars = 200;

    /// <summary>The first quoted line that is not in the file, and the real line nearest to it, if any is.</summary>
    public sealed record Mismatch(int QuotedNumber, string QuotedText, int? ClosestNumber, string? ClosestText);

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();

    /// <summary>Null when every quoted line is in the file (the lines then differ in order or in the blank lines between them).</summary>
    public static Mismatch? Find(string content, string quoted)
    {
        var lines = content.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var quotedLines = quoted.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

        var missing = Array.FindIndex(quotedLines, line => Identifiers(line).Count > 0 && !lines.Contains(line));
        if (missing < 0) return null;

        var closest = Nearest(lines, Identifiers(quotedLines[missing]));
        return new Mismatch(
            missing + 1,
            Shorten(quotedLines[missing]),
            closest is null ? null : closest + 1,
            closest is null ? null : Shorten(lines[closest.Value]));
    }

    private static int? Nearest(string[] lines, HashSet<string> wanted)
    {
        var identifiersOfLine = lines.Select(Identifiers).ToArray();
        var scores = new double[lines.Length];
        foreach (var identifier in wanted)
        {
            var holders = identifiersOfLine.Count(found => found.Contains(identifier));
            for (var i = 0; i < lines.Length; i++)
                if (identifiersOfLine[i].Contains(identifier)) scores[i] += 1.0 / holders;
        }

        var best = Array.IndexOf(scores, scores.Max());
        return scores[best] > 0 ? best : null;
    }

    private static HashSet<string> Identifiers(string text) =>
        Identifier().Matches(text).Select(match => match.Value).ToHashSet();

    private static string Shorten(string line) => line.Length <= MaxChars ? line : line[..MaxChars] + "...";
}
