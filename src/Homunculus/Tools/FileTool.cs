using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

/// <summary>Base for tools that work on one file or directory given by a "path" argument.</summary>
public abstract class FileTool(ProjectDirectory project) : ITool
{
    protected ProjectDirectory Project { get; } = project;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonObject Parameters { get; }
    public abstract ToolAccess Access { get; }

    public virtual string? TargetPath(JsonElement arguments) => Project.Resolve(arguments.Text("path"));

    public Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation) =>
        Task.FromResult(Run(arguments, TargetPath(arguments)!));

    protected abstract string Run(JsonElement arguments, string path);
}
