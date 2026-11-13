using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class ReadFileTool(ProjectDirectory project) : FileTool(project)
{
    public const string ToolName = "read_file";

    public override string Name => ToolName;
    public override string Description => $"Read a text file (at most {Limits.OutputChars} characters).";
    public override JsonObject Parameters => Schema.Of(Parameter.Required("path", "File to read."));
    protected override ToolAccess Access => ToolAccess.Read;

    protected override string Run(JsonElement arguments, string path)
    {
        if (Directory.Exists(path))
            throw new ToolException($"{Relative(path)} is a directory, not a file. Use list_files to see what is in it.");
        if (!File.Exists(path))
            throw new ToolException($"File not found: {path}");

        return File.ReadAllText(path).Truncate(Limits.OutputChars);
    }
}
