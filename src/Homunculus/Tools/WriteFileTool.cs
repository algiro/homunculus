using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class WriteFileTool(ProjectDirectory project, ChangedFiles? changes = null, ReadFiles? reads = null) : FileTool(project)
{
    public const string ToolName = "write_file";

    private readonly NewFileWriter _writer = new(project, changes, reads);

    public override string Name => ToolName;
    public override string Description =>
        "Create a new file with the given content (the whole file; missing folders are created). " +
        "It refuses a file that already exists: change those with edit_file or insert_lines.";
    public override JsonObject Parameters => Schema.Of(
        Parameter.Required("path", "Path of the new file."),
        Parameter.Required("content", "The complete text of the file."));
    protected override ToolAccess Access => ToolAccess.Write;

    protected override string Run(JsonElement arguments, string path)
    {
        var content = arguments.Text("content");

        RefuseDirectory(path);
        if (EscapedLineBreaks.Look(content))
            throw new ToolException(EscapedLineBreaks.Complaint("content"));

        return _writer.Create(path, content, "To change it, use edit_file or insert_lines.");
    }
}
