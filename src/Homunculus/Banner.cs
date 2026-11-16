using System.Globalization;
using System.Text;

namespace Homunculus;

/// <summary>What the start-up banner tells the user about this session. <paramref name="JudgeModel"/> is shown only when it differs from the model.</summary>
public sealed record BannerDetails(string Model, string Server, string Project, string Mode, string? JudgeModel = null);

/// <summary>
/// How much the banner may draw: <see cref="Text"/> for output that is not a console (a pipe or a file), <see cref="Art"/>
/// for a console without colours, <see cref="Color"/> for one that understands 24-bit colour.
/// </summary>
public enum BannerStyle { Text, Art, Color }

/// <summary>
/// The banner shown when the program starts: the homunculus in its flask (the pixel art of docs/homunculus.svg), the name in
/// block letters, and what this session talks to and works on, in a rounded box. It shrinks to fit a narrow console: first the
/// flask goes, then the block letters.
/// </summary>
public static class Banner
{
    public const string Tagline = "a tiny coding agent in plain .NET";
    public const string Hint = "/help for commands · /exit to quit · ctrl-c stops a turn";

    private readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb Parse(string hex) =>
            new(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16));
    }

    /// <summary>One run of text in one colour; null colours leave the console's own.</summary>
    private readonly record struct Span(string Text, Rgb? Foreground = null, Rgb? Background = null);

    private static readonly Rgb Mint = Rgb.Parse("#d6fff0");
    private static readonly Rgb Potion = Rgb.Parse("#5fcf9f");
    private static readonly Rgb Gold = Rgb.Parse("#ffd45e");
    private static readonly Rgb Muted = Rgb.Parse("#8a8399");
    private static readonly Rgb Value = Rgb.Parse("#e9f3f5");

    /// <summary>The flask, one letter per pixel from <see cref="Palette"/>; '.' is empty.</summary>
    private static readonly string[] Flask =
    [
        "..............j.j...",
        ".......eeeeee..j....",
        ".......ceeeec.......",
        ".......akhhha.......",
        ".......akhhha.......",
        "......akhhhhha......",
        ".....akhhhhhhha.....",
        "....akhhhhhhhhha....",
        "...akhhhhhhhhhhha...",
        "..adddddddddddddda..",
        ".abbbbbbiiiibbbbbba.",
        "abbbbbbiiiiiibbbbbba",
        "abbfbbbiaiiaibbbbbba",
        "abbbbbbiiiiiibbbfbba",
        "abbbbbbbiiiibbbbbbba",
        "abbbbbgigiigigbfbbba",
        "abbbbbbgiiiigbbbbbba",
        "abbbfbbbgiigbbbbbbba",
        ".abbbbbbgbbgbbbbbba.",
        "..abbbbbbbbbbfbbba..",
        "...abbbbbbbbbbbba...",
        "....abbbbbbbbbba....",
        ".....aaaaaaaaaa.....",
    ];

    private static readonly Dictionary<char, Rgb> Palette = new()
    {
        ['a'] = Rgb.Parse("#2d2438"), ['b'] = Rgb.Parse("#5fcf9f"), ['c'] = Rgb.Parse("#8f5f33"), ['d'] = Rgb.Parse("#9be8c1"),
        ['e'] = Rgb.Parse("#c68a52"), ['f'] = Rgb.Parse("#d6fff0"), ['g'] = Rgb.Parse("#d99c72"), ['h'] = Rgb.Parse("#e9f3f5"),
        ['i'] = Rgb.Parse("#f4c79b"), ['j'] = Rgb.Parse("#ffd45e"), ['k'] = Rgb.Parse("#ffffff"),
    };

    private static readonly Dictionary<char, string[]> Letters = new()
    {
        ['H'] = ["█  █",
                 "█  █",
                 "████",
                 "█  █",
                 "█  █"],

        ['O'] = [" ██ ",
                 "█  █",
                 "█  █",
                 "█  █",
                 " ██ "],

        ['M'] = ["█   █",
                 "██ ██",
                 "█ █ █",
                 "█   █",
                 "█   █"],

        ['U'] = ["█  █",
                 "█  █",
                 "█  █",
                 "█  █",
                 " ██ "],

        ['N'] = ["█  █",
                 "██ █",
                 "█ ██",
                 "█  █",
                 "█  █"],

        ['C'] = [" ███",
                 "█   ",
                 "█   ",
                 "█   ",
                 " ███"],

        ['L'] = ["█   ",
                 "█   ",
                 "█   ",
                 "█   ",
                 "████"],

        ['S'] = [" ███",
                 "█   ",
                 " ██ ",
                 "   █",
                 "███ "],
    };

    /// <summary>The name in block letters, one string per row, letters one column apart.</summary>
    public static IReadOnlyList<string> Title { get; } =
        Enumerable.Range(0, 5).Select(row => string.Join(' ', "HOMUNCULUS".Select(letter => Letters[letter][row]))).ToList();

    private static int FlaskWidth => Flask[0].Length;
    private static int TitleWidth => Title[0].Length;

    /// <summary>Space between the box's border and its content, and between the flask and the text.</summary>
    private const int Padding = 1;
    private const int Gap = 3;

    /// <summary>The whole banner as lines to print, the widest of which is at most <paramref name="width"/> characters when it can be.</summary>
    public static string Render(BannerDetails details, BannerStyle style, int width)
    {
        if (style == BannerStyle.Text) return string.Join(Environment.NewLine, TextLines(details));

        var withFlask = style == BannerStyle.Color && width >= Frame(FlaskWidth + Gap + TitleWidth);
        var withTitle = width >= Frame(TitleWidth);
        var textWidth = withTitle ? TitleWidth : Math.Max(width - Frame(0), 20);

        var right = new List<List<Span>>();
        if (withTitle)
        {
            right.Add([]);
            right.AddRange(Title.Select(GradientRow));
            right.Add([]);
        }
        else
        {
            right.Add([new("homunculus", Potion)]);
        }

        right.Add([new(Fit(Tagline, textWidth), Mint)]);
        right.Add([]);
        right.AddRange(Details(details, textWidth));

        var left = withFlask ? FlaskRows() : [];
        var leftWidth = withFlask ? FlaskWidth + Gap : 0;
        var rows = Math.Max(left.Count, right.Count);
        var inner = leftWidth + textWidth;

        var lines = new List<List<Span>> { Border('╭', '╮', inner) };
        for (var i = 0; i < rows; i++)
        {
            var row = new List<Span> { new("│" + new string(' ', Padding), Muted) };
            if (withFlask)
            {
                row.AddRange(i < left.Count ? left[i] : [new(new string(' ', FlaskWidth))]);
                row.Add(new(new string(' ', Gap)));
            }

            var text = i < right.Count ? right[i] : [];
            row.AddRange(text);
            row.Add(new(new string(' ', textWidth - text.Sum(span => span.Text.Length))));
            row.Add(new(new string(' ', Padding) + "│", Muted));
            lines.Add(row);
        }

        lines.Add(Border('╰', '╯', inner));
        lines.Add([new(" " + Fit(Hint, Math.Max(width - 1, 20)), Muted)]);

        return string.Join(Environment.NewLine, lines.Select(line => Write(line, style == BannerStyle.Color)));
    }

    /// <summary>The banner for a pipe or a file: plain lines a log can keep.</summary>
    private static IEnumerable<string> TextLines(BannerDetails details)
    {
        yield return $"homunculus - {Tagline}";
        yield return $"model: {details.Model} ({details.Server})";
        if (details.JudgeModel is { } judge) yield return $"judge: {judge}";
        yield return $"project: {details.Project}";
        yield return $"mode: {details.Mode}";
        yield return "Type /help for commands, /exit to quit.";
    }

    private static int Frame(int content) => content + 2 * (Padding + 1);

    private static List<Span> Border(char left, char right, int inner) =>
        [new(left + new string('─', inner + 2 * Padding) + right, Muted)];

    private static IEnumerable<List<Span>> Details(BannerDetails details, int width)
    {
        const int labelWidth = 9;
        List<Span> Line(string label, string value, Rgb colour) =>
            [new(label.PadRight(labelWidth), Muted), new(Fit(value, width - labelWidth, keepEnd: label == "project"), colour)];

        yield return Line("model", $"{details.Model} · {details.Server}", Value);
        if (details.JudgeModel is { } judge) yield return Line("judge", judge, Value);
        yield return Line("project", details.Project, Value);
        yield return Line("mode", details.Mode, details.Mode == "allow-all" ? Gold : Value);
    }

    /// <summary>Shortens a text to <paramref name="width"/> with "…": a path keeps its end, where the project's own name is.</summary>
    private static string Fit(string text, int width, bool keepEnd = false) =>
        text.Length <= width ? text
        : keepEnd ? "…" + text[^(width - 1)..]
        : text[..(width - 1)] + "…";

    /// <summary>One row of the title, shading from mint on the left to the potion's green on the right.</summary>
    private static List<Span> GradientRow(string row) =>
        row.Select((character, column) => new Span(character.ToString(), Blend(Mint, Potion, column / (double)(row.Length - 1)))).ToList();

    private static Rgb Blend(Rgb from, Rgb to, double amount) => new(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>The flask two pixel rows to a line: the upper pixel is the character's colour, the lower one its background.</summary>
    private static List<List<Span>> FlaskRows()
    {
        var rows = new List<List<Span>>();
        for (var y = 0; y < Flask.Length; y += 2)
        {
            var row = new List<Span>();
            for (var x = 0; x < FlaskWidth; x++)
            {
                Rgb? top = Palette.TryGetValue(Flask[y][x], out var upper) ? upper : null;
                Rgb? bottom = y + 1 < Flask.Length && Palette.TryGetValue(Flask[y + 1][x], out var lower) ? lower : null;
                row.Add((top, bottom) switch
                {
                    (null, null) => new(" "),
                    (null, { } only) => new("▄", only),
                    ({ } only, null) => new("▀", only),
                    ({ } upperColour, { } lowerColour) when upperColour == lowerColour => new("█", upperColour),
                    ({ } upperColour, { } lowerColour) => new("▀", upperColour, lowerColour),
                });
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>The line as text, with 24-bit colour escapes when <paramref name="colour"/> is set; each span resets after itself.</summary>
    private static string Write(List<Span> line, bool colour)
    {
        var text = new StringBuilder();
        foreach (var span in line)
        {
            if (!colour || span is { Foreground: null, Background: null })
            {
                text.Append(span.Text);
                continue;
            }

            if (span.Foreground is { } fg) text.Append(CultureInfo.InvariantCulture, $"\e[38;2;{fg.R};{fg.G};{fg.B}m");
            if (span.Background is { } bg) text.Append(CultureInfo.InvariantCulture, $"\e[48;2;{bg.R};{bg.G};{bg.B}m");
            text.Append(span.Text).Append("\e[0m");
        }

        return text.ToString().TrimEnd();
    }
}
