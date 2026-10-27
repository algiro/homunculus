using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class InsertLinesTool(ProjectDirectory project, ChangedFiles? changes = null) : FileTool(project)
{
    public const string ToolName = "insert_lines";

    private const int ContextLines = 2;
    private const int MaxLineChars = 200;
    private const int MaxListedLines = 5;

    public override string Name => ToolName;
    public override string Description =>
        "Insert lines before or after an existing line of a file, for example to register something next to its siblings. " +
        "Give the existing line as 'line' (indentation does not matter); the new lines take its indentation.";
    public override JsonObject Parameters => Schema.Of(
        Parameter.Required("path", "File to change."),
        Parameter.Required("line", "An existing line of the file to anchor on, copied from it. It must identify one line."),
        Parameter.Required("text", "The lines to insert."),
        Parameter.Optional("position", "'after' (the default) or 'before' the anchor line."));
    protected override ToolAccess Access => ToolAccess.Write;

    protected override string Run(JsonElement arguments, string path)
    {
        var anchor = arguments.Text("line").Trim();
        var text = arguments.Text("text");
        var before = IsBefore(arguments.OptionalText("position"));

        RefuseDirectory(path);
        if (!File.Exists(path))
            throw new ToolException($"File not found: {path}. To create a file, use edit_file with an empty old_str.");
        if (anchor.Length == 0 || anchor.Contains('\n'))
            throw new ToolException("line must be one existing line of the file, copied from it.");
        if (text.Trim().Length == 0)
            throw new ToolException("text is empty; give the lines to insert.");
        if (EscapedLineBreaks.Look(text))
            throw new ToolException(EscapedLineBreaks.Complaint("text"));

        var content = File.ReadAllText(path);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = content.EndsWith('\n');
        var lines = content.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

        var at = FindAnchor(lines, anchor, content);
        var inserted = WithIndentation(text, IndentationOf(lines[at]));
        var index = before ? at : at + 1;
        lines.InsertRange(index, inserted);

        File.WriteAllText(path, string.Join(newline, lines) + (endsWithNewline ? newline : ""));
        changes?.Edited(path);
        return Describe(path, lines, index, inserted.Count, before, before ? at + inserted.Count + 1 : at + 1);
    }

    private static bool IsBefore(string? position) => position?.Trim().ToLowerInvariant() switch
    {
        null or "" or "after" => false,
        "before" => true,
        _ => throw new ToolException("position must be 'after' or 'before'."),
    };

    /// <summary>The one line that is the anchor: the line equal to it ignoring indentation, else the one line that contains it.</summary>
    private static int FindAnchor(List<string> lines, string anchor, string content)
    {
        var exact = Matching(lines, line => line.Trim() == anchor);
        var candidates = exact.Count > 0 ? exact : Matching(lines, line => line.Contains(anchor, StringComparison.Ordinal));

        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count > 1)
            throw new ToolException(
                $"The line '{Shorten(anchor)}' matches {candidates.Count} lines (line numbers {string.Join(", ", candidates.Take(MaxListedLines).Select(i => i + 1))}" +
                $"{(candidates.Count > MaxListedLines ? ", ..." : "")}); anchor on a line that appears once.");

        var message = $"The line '{Shorten(anchor)}' is not in the file.";
        if (ClosestLine.Find(content, anchor) is { ClosestNumber: { } number, ClosestText: { } closest })
            message += $" The closest line is line {number}: {closest}";
        throw new ToolException(message);
    }

    private static List<int> Matching(List<string> lines, Func<string, bool> matches) =>
        Enumerable.Range(0, lines.Count).Where(i => matches(lines[i])).ToList();

    private static string IndentationOf(string line) => line[..(line.Length - line.TrimStart().Length)];

    /// <summary>The lines of <paramref name="text"/> with their common indentation replaced by <paramref name="indentation"/>; deeper lines stay deeper.</summary>
    private static List<string> WithIndentation(string text, string indentation)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd()).ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        var common = lines.Where(line => line.Length > 0).Select(line => IndentationOf(line).Length).DefaultIfEmpty(0).Min();
        return lines.Select(line => line.Length == 0 ? "" : indentation + line[common..]).ToList();
    }

    /// <summary>What the file looks like around the insertion, so the model can check it without reading the whole file.</summary>
    private string Describe(string path, List<string> lines, int index, int count, bool before, int anchorNumber)
    {
        var from = Math.Max(0, index - ContextLines);
        var to = Math.Min(lines.Count, index + count + ContextLines);
        var shown = Enumerable.Range(from, to - from)
            .Select(i => $"{(i >= index && i < index + count ? '+' : ' ')} {i + 1,4}: {Shorten(lines[i])}");

        return $"Inserted {count} line{(count == 1 ? "" : "s")} {(before ? "before" : "after")} line {anchorNumber} of {Relative(path)}:\n" +
               string.Join('\n', shown);
    }

    private static string Shorten(string line) => line.Length <= MaxLineChars ? line : line[..MaxLineChars] + "...";
}
