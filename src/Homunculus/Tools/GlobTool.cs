using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class GlobTool(ProjectDirectory project) : FileTool(project)
{
    public const string ToolName = "glob";

    public override string Name => ToolName;
    public override string Description =>
        $"Find files by name pattern, such as '**/*.cs' or 'src/**/Version*.cs' (at most {Limits.SearchResults} results). " +
        "A pattern without '/' matches in any folder. Skips bin, obj and .git.";
    public override JsonObject Parameters => Schema.Of(
        Parameter.Required("pattern", "File pattern: * and ? stay in one folder, ** crosses folders, {a,b} is either."),
        Parameter.Optional("path", "Folder to search in; defaults to the project root."));
    protected override ToolAccess Access => ToolAccess.Read;

    protected override string PathOf(JsonElement arguments) =>
        Project.Resolve(arguments.OptionalText("path") ?? ".");

    protected override string Run(JsonElement arguments, string path)
    {
        var pattern = new GlobPattern(arguments.Text("pattern"));
        if (!Directory.Exists(path))
            throw new ToolException(File.Exists(path)
                ? $"{Relative(path)} is a file, not a folder."
                : $"Not a directory: {path}");

        var found = SearchableFiles.Under(path)
            .Where(file => pattern.IsMatch(Path.GetRelativePath(path, file).Replace('\\', '/')))
            .Select(Relative)
            .Take(Limits.SearchResults + 1)
            .ToList();

        if (found.Count == 0) return "No files match.";
        return found.Count > Limits.SearchResults
            ? string.Join('\n', found.Take(Limits.SearchResults)) + "\n[more files not shown; use a narrower pattern or path]"
            : string.Join('\n', found);
    }
}
