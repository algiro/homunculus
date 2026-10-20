using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class EditFileTool(ProjectDirectory project) : FileTool(project)
{
    public override string Name => "edit_file";
    public override string Description =>
        "Replace the single exact occurrence of old_str with new_str in a file. " +
        "With an empty old_str, create a new file containing new_str.";
    public override JsonObject Parameters => Schema.Object(
        Parameter.Required("path", "File to edit or create."),
        Parameter.Required("old_str", "Exact text to replace; must match exactly once. Empty to create a file."),
        Parameter.Required("new_str", "Replacement text."));
    protected override ToolAccess Access => ToolAccess.Write;

    protected override string Run(JsonElement arguments, string path)
    {
        var oldText = arguments.Text("old_str");
        var newText = arguments.Text("new_str");

        if (oldText.Length == 0)
            return Create(path, newText);

        if (!File.Exists(path))
            throw new ToolException($"File not found: {path}");

        var content = File.ReadAllText(path);
        var matches = CountOccurrences(content, oldText);
        if (matches == 0)
            throw new ToolException("old_str was not found in the file.");
        if (matches > 1)
            throw new ToolException($"old_str matches {matches} places; add more context so it matches exactly once.");

        File.WriteAllText(path, content.Replace(oldText, newText, StringComparison.Ordinal));
        return $"Edited {path}";
    }

    private static string Create(string path, string content)
    {
        if (File.Exists(path))
            throw new ToolException($"File already exists: {path}. Use a non-empty old_str to edit it.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return $"Created {path}";
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
