using System.Text;
using System.Text.RegularExpressions;

namespace Homunculus;

/// <summary>
/// Draws the model's markdown answers for a console: headings, lists, quotes, rules, code blocks and tables, with bold, italic,
/// code and links inside lines. <see cref="BannerStyle.Color"/> styles them with escape codes, <see cref="BannerStyle.Art"/>
/// lays them out without colour, and <see cref="BannerStyle.Text"/> leaves the markdown as it is, for a pipe or a file.
/// A table too wide for the console becomes one block per row, each cell under its column's name.
/// </summary>
public static partial class Markdown
{
    public static string Render(string markdown, BannerStyle style, int width)
    {
        if (style == BannerStyle.Text) return markdown;

        var colour = style == BannerStyle.Color;
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var output = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (Fence().Match(line) is { Success: true } fence)
            {
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(fence.Groups[1].Value, StringComparison.Ordinal); i++)
                    code.Add(lines[i]);
                output.AddRange(CodeBlock(code, fence.Groups[2].Value.Trim(), colour));
                continue;
            }

            if (IsTableRow(line) && i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1]))
            {
                var rows = new List<string[]> { Cells(line) };
                for (i += 2; i < lines.Length && IsTableRow(lines[i]); i++) rows.Add(Cells(lines[i]));
                i--;
                output.AddRange(Table(rows, colour, width));
                continue;
            }

            output.Add(Line(line, colour, width));
        }

        return string.Join(Environment.NewLine, output);
    }

    [GeneratedRegex(@"^\s*(```+|~~~+)(.*)$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)[-*+]\s+(\[[ xX]\]\s+)?(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(\s*)(\d+[.)])\s+(.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\s*([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    private static string Line(string line, bool colour, int width)
    {
        if (Heading().Match(line) is { Success: true } heading)
        {
            var text = Inline(heading.Groups[2].Value, colour);
            return colour ? $"{Ansi.Bold}{Ansi.Green}{text}{Ansi.Reset}" : text.ToUpperInvariant();
        }

        if (Rule().IsMatch(line))
            return Paint(new string('─', Math.Min(width - 1, 60)), Ansi.Grey, colour);

        if (line.TrimStart().StartsWith('>'))
            return Paint("│ ", Ansi.Grey, colour) + Inline(line.TrimStart()[1..].TrimStart(), colour);

        if (Bullet().Match(line) is { Success: true } bullet)
        {
            var box = bullet.Groups[2].Value.Trim() is { Length: > 0 } check ? (check[1] == ' ' ? "☐ " : "☑ ") : "";
            return $"{bullet.Groups[1].Value}{Paint("•", Ansi.Green, colour)} {box}{Inline(bullet.Groups[3].Value, colour)}";
        }

        if (Numbered().Match(line) is { Success: true } numbered)
            return $"{numbered.Groups[1].Value}{Paint(numbered.Groups[2].Value, Ansi.Green, colour)} {Inline(numbered.Groups[3].Value, colour)}";

        return Inline(line, colour);
    }

    private static IEnumerable<string> CodeBlock(List<string> code, string language, bool colour)
    {
        if (!colour)
        {
            foreach (var line in code) yield return "    " + line;
            yield break;
        }

        var inner = Math.Max(code.Count == 0 ? 0 : code.Max(line => line.Length), language.Length) + 2;
        if (language.Length > 0) yield return $"  {Ansi.Grey}{language}{Ansi.Reset}";
        foreach (var line in code)
            yield return $"  {Ansi.CodeBackground}{Ansi.Mint} {line.PadRight(inner - 1)}{Ansi.Reset}";
    }

    private static bool IsTableRow(string line) => line.TrimStart().StartsWith('|') || (line.Contains('|') && line.Trim().EndsWith('|'));

    private static string[] Cells(string row)
    {
        var text = row.Trim();
        if (text.StartsWith('|')) text = text[1..];
        if (text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal)) text = text[..^1];
        return [.. SplitCells(text).Select(cell => cell.Trim())];
    }

    /// <summary>Splits on '|' outside inline code, so a cell can show "a|b" in backticks; "\|" stays a bar too.</summary>
    private static IEnumerable<string> SplitCells(string text)
    {
        var cell = new StringBuilder();
        var inCode = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '|') { cell.Append('|'); i++; continue; }
            if (c == '`') inCode = !inCode;
            if (c == '|' && !inCode) { yield return cell.ToString(); cell.Clear(); continue; }
            cell.Append(c);
        }

        yield return cell.ToString();
    }

    private static IEnumerable<string> Table(List<string[]> rows, bool colour, int width)
    {
        var columns = rows.Max(row => row.Length);
        var cells = rows.Select(row => Enumerable.Range(0, columns).Select(c => c < row.Length ? row[c] : "").ToArray()).ToList();
        var widths = Enumerable.Range(0, columns).Select(c => cells.Max(row => Plain(row[c]).Length)).ToArray();

        // "│ a │ b │" takes three characters per column plus one.
        if (widths.Sum() + 3 * columns + 1 > width)
        {
            foreach (var line in Records(cells, colour)) yield return line;
            yield break;
        }

        string Row(string[] row, bool header) => Paint("│", Ansi.Grey, colour) + string.Concat(row.Select((cell, c) =>
        {
            var text = Inline(cell, colour);
            if (header && colour) text = Ansi.Bold + text + Ansi.Reset;
            return " " + text + new string(' ', widths[c] - Plain(cell).Length) + " " + Paint("│", Ansi.Grey, colour);
        }));

        string Border(char left, char middle, char right) =>
            Paint(left + string.Join(middle, widths.Select(w => new string('─', w + 2))) + right, Ansi.Grey, colour);

        yield return Border('┌', '┬', '┐');
        yield return Row(cells[0], header: true);
        yield return Border('├', '┼', '┤');
        foreach (var row in cells.Skip(1)) yield return Row(row, header: false);
        yield return Border('└', '┴', '┘');
    }

    /// <summary>A table too wide to draw: each row as its own block, every cell named by its column.</summary>
    private static IEnumerable<string> Records(List<string[]> cells, bool colour)
    {
        var names = cells[0].Select(Plain).ToArray();
        var labelWidth = names.Max(name => name.Length);
        foreach (var row in cells.Skip(1))
        {
            for (var c = 0; c < row.Length; c++)
            {
                var label = names[c].PadRight(labelWidth);
                yield return $"{(c == 0 ? Paint("•", Ansi.Green, colour) : " ")} {Paint(label, Ansi.Grey, colour)}  {Inline(row[c], colour)}";
            }
        }
    }

    private static string Paint(string text, string code, bool colour) => colour ? code + text + Ansi.Reset : text;

    /// <summary>The text a line shows once its markers are gone, to measure it.</summary>
    public static string Plain(string text) => Inline(text, colour: false);

    /// <summary>
    /// Inline markers: `code`, **bold**, *italic*, [text](url). Underscores and asterisks only count at word edges, so names
    /// such as read_file or 2*3*4 stay as they are. Without colour the markers go and a link keeps its address in brackets.
    /// </summary>
    public static string Inline(string text, bool colour)
    {
        var output = new StringBuilder();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '`' && text.IndexOf('`', i + 1) is var end and > 0)
            {
                var code = text[(i + 1)..end];
                output.Append(colour ? $"{Ansi.Mint}{code}{Ansi.Reset}" : code);
                i = end + 1;
                continue;
            }

            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c && AtStart(text, i, 2)
                && Closing(text, i + 2, new string(c, 2)) is var strongEnd and > 0)
            {
                var inner = Inline(text[(i + 2)..strongEnd], colour);
                output.Append(colour ? $"{Ansi.Bold}{inner}{Ansi.Reset}" : inner);
                i = strongEnd + 2;
                continue;
            }

            if ((c == '*' || c == '_') && AtStart(text, i, 1) && Closing(text, i + 1, c.ToString()) is var emEnd and > 0)
            {
                var inner = Inline(text[(i + 1)..emEnd], colour);
                output.Append(colour ? $"{Ansi.Italic}{inner}{Ansi.Reset}" : inner);
                i = emEnd + 1;
                continue;
            }

            if (c == '[' && Link().Match(text, i) is { Success: true } link && link.Index == i)
            {
                var label = link.Groups[1].Value;
                var url = link.Groups[2].Value;
                output.Append(colour
                    ? $"{Ansi.Underline}{label}{Ansi.Reset}{(label == url ? "" : $" {Ansi.Grey}({url}){Ansi.Reset}")}"
                    : label == url ? url : $"{label} ({url})");
                i += link.Length;
                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    /// <summary>A marker opens emphasis when nothing word-like is before it and text follows it directly.</summary>
    private static bool AtStart(string text, int at, int length) =>
        (at == 0 || !char.IsLetterOrDigit(text[at - 1])) && at + length < text.Length && !char.IsWhiteSpace(text[at + length]);

    /// <summary>Where the closing marker is: right after text, with nothing word-like after it; -1 when there is none.</summary>
    private static int Closing(string text, int from, string marker)
    {
        for (var at = text.IndexOf(marker, from + 1, StringComparison.Ordinal); at > 0; at = text.IndexOf(marker, at + 1, StringComparison.Ordinal))
        {
            var end = at + marker.Length;
            if (!char.IsWhiteSpace(text[at - 1]) && (end >= text.Length || EndsEmphasis(text[end], marker[0]))) return at;
        }

        return -1;
    }

    /// <summary>
    /// What may follow a closing marker. Underscores are stricter than markdown asks, since names such as __init__.py are far
    /// more common in a coding agent's answers than underlined words before a dot.
    /// </summary>
    private static bool EndsEmphasis(char next, char marker) =>
        marker == '_' ? char.IsWhiteSpace(next) || ",;:!?)".Contains(next) : !char.IsLetterOrDigit(next);
}
