using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class EditFileTool(ProjectDirectory project, ChangedFiles? changes = null, ReadFiles? reads = null) : FileTool(project)
{
    public override string Name => "edit_file";
    public override string Description =>
        "Replace the single exact occurrence of old_str with new_str in a file; to add something, keep the old text in new_str too. " +
        "With an empty old_str, create a new file containing new_str.";
    public override JsonObject Parameters => Schema.Of(
        Parameter.Required("path", "File to edit or create."),
        Parameter.Required("old_str", "Exact text to replace; must match exactly once. Empty to create a file."),
        Parameter.Required("new_str", "Replacement text."));
    protected override ToolAccess Access => ToolAccess.Write;

    protected override string Run(JsonElement arguments, string path)
    {
        var oldText = arguments.Text("old_str");
        var newText = arguments.Text("new_str");

        if (Directory.Exists(path))
            throw new ToolException($"{Relative(path)} is a directory, not a file. Give the path of a file, for example {Relative(path)}/<file name>.");

        if (LooksDoubleEscaped(newText) && (oldText.Length == 0 || oldText.Contains('\n')))
            throw new ToolException(EscapedLineBreaks("new_str"));

        if (oldText.Length == 0)
            return Create(path, newText);

        if (!File.Exists(path))
            throw new ToolException($"File not found: {path}. To create a file, use an empty old_str.");

        var content = File.ReadAllText(path);
        (oldText, newText) = MatchLineEndings(content, oldText, newText);
        var matches = CountOccurrences(content, oldText);
        if (matches == 0)
            throw new ToolException(NotFound(content, oldText));
        if (matches > 1)
            throw new ToolException($"old_str matches {matches} places; add more context so it matches exactly once.");

        var edited = content.Replace(oldText, newText, StringComparison.Ordinal);
        File.WriteAllText(path, edited);
        changes?.Edited(path);
        return $"Edited {path}{RemovedNames(oldText, edited)}";
    }

    /// <summary>A model that meant to add a line but replaced one loses the names that were on it; say so, since it may not notice.</summary>
    private static string RemovedNames(string oldText, string editedContent)
    {
        var stillThere = Identifiers.In(editedContent);
        var gone = Identifiers.In(oldText).Where(name => !stillThere.Contains(name)).Order(StringComparer.Ordinal).Take(5).ToArray();
        return gone.Length == 0
            ? ""
            : $". Warning: {string.Join(", ", gone)} no longer appear{(gone.Length == 1 ? "s" : "")} in this file; " +
              "if you meant to add something, edit again and keep the old text in new_str.";
    }

    private static string NotFound(string content, string oldText)
    {
        var message = "old_str was not found in the file. Read the file again and copy the text exactly, including indentation; " +
                      "prefer one short line that appears only once.";
        if (LooksDoubleEscaped(oldText))
            message += " " + EscapedLineBreaks("old_str");
        if (ClosestLine.Find(content, oldText) is { } mismatch)
        {
            message += $" Line {mismatch.QuotedNumber} of old_str is not in the file: {mismatch.QuotedText}";
            if (mismatch.ClosestNumber is { } number)
                message += $" The closest line in the file is line {number}: {mismatch.ClosestText}";
        }
        else if (ClosestLine.EveryLineExists(content, oldText))
        {
            message += " Every line of old_str is in the file, but not together as one block: the order, the indentation " +
                       "or the blank lines between them differ. Use just one of those lines as old_str.";
        }

        return message;
    }

    /// <summary>A model that escapes its own escapes sends the characters backslash and n where it means a line break.</summary>
    private static bool LooksDoubleEscaped(string text) =>
        !text.Contains('\n') && CountOccurrences(text, "\\n") >= 2;

    private static string EscapedLineBreaks(string argument) =>
        $"{argument} has no line breaks but contains the two characters backslash and n. Write real line breaks, not the characters \\n.";

    /// <summary>Models write "\n". In a file with "\r\n" line endings their multi-line text would never match, so it is converted.</summary>
    private static (string Old, string New) MatchLineEndings(string content, string oldText, string newText) =>
        content.Contains("\r\n", StringComparison.Ordinal) && !oldText.Contains('\r')
            ? (WithWindowsLineEndings(oldText), WithWindowsLineEndings(newText))
            : (oldText, newText);

    private static string WithWindowsLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private string Create(string path, string content)
    {
        if (File.Exists(path))
            throw new ToolException($"File already exists: {path}. Use a non-empty old_str to edit it.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        changes?.Created(path);
        return $"Created {path}{UnreadNeighboursNote(path)}";
    }

    /// <summary>A new file that should follow its neighbours is better written after looking at one of them.</summary>
    private string UnreadNeighboursNote(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var hasNeighbours = Directory.EnumerateFiles(folder).Any(other => other != path);
        return reads is null || !hasNeighbours || reads.HasReadAnyIn(folder)
            ? ""
            : $". You have not read any other file in {Relative(folder)} yet; if this file should follow the ones there, read one and compare.";
    }

    private static int CountOccurrences(string content, string text)
    {
        var count = 0;
        for (var at = content.IndexOf(text, StringComparison.Ordinal); at >= 0;
             at = content.IndexOf(text, at + text.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
