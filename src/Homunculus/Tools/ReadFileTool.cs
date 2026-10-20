using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class ReadFileTool(ProjectDirectory project) : FileTool(project)
{
    public override string Name => "read_file";
    public override string Description => $"Read a text file (at most {Limits.OutputChars} characters).";
    public override JsonObject Parameters => Schema.Strings(1, ("path", "File to read."));
    protected override ToolAccess Access => ToolAccess.Read;

    protected override string Run(JsonElement arguments, string path)
    {
        if (!File.Exists(path))
            throw new ToolException($"File not found: {path}");

        return Text.Truncate(File.ReadAllText(path), Limits.OutputChars);
    }
}
