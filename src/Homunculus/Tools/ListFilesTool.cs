using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class ListFilesTool(ProjectDirectory project) : FileTool(project)
{
    public override string Name => "list_files";
    public override string Description => $"List the entries of a directory (at most {Limits.ListEntries}). Directories end with '/'.";
    public override JsonObject Parameters => Schema.Strings(0, ("path", "Directory to list; defaults to the project root."));
    public override ToolAccess Access => ToolAccess.Read;

    public override string? TargetPath(JsonElement arguments) =>
        Project.Resolve(arguments.OptionalText("path") ?? ".");

    protected override string Run(JsonElement arguments, string path)
    {
        if (!Directory.Exists(path))
            throw new ToolException($"Not a directory: {path}");

        var entries = Directory.EnumerateFileSystemEntries(path)
            .Select(entry => Path.GetFileName(entry) + (Directory.Exists(entry) ? "/" : ""))
            .Order()
            .ToList();

        var shown = string.Join('\n', entries.Take(Limits.ListEntries));
        return entries.Count > Limits.ListEntries
            ? $"{shown}\n[{entries.Count - Limits.ListEntries} more entries not shown]"
            : shown;
    }
}
