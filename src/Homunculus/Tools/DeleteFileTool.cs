using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class DeleteFileTool(ProjectDirectory project) : FileTool(project)
{
    public override string Name => "delete_file";
    public override string Description => "Delete a file. Directories are refused.";
    public override JsonObject Parameters => Schema.Object(Parameter.Required("path", "File to delete."));
    protected override ToolAccess Access => ToolAccess.Write;

    protected override string Run(JsonElement arguments, string path)
    {
        if (Directory.Exists(path))
            throw new ToolException($"Refusing to delete a directory: {path}");
        if (!File.Exists(path))
            throw new ToolException($"File not found: {path}");

        File.Delete(path);
        return $"Deleted {path}";
    }
}
