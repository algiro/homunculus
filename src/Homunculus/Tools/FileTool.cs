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
    protected abstract ToolAccess Access { get; }

    public ToolEffect EffectOf(JsonElement arguments) => new(Access, PathOf(arguments));

    public Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation) =>
        Task.FromResult(Run(arguments, PathOf(arguments)));

    protected virtual string PathOf(JsonElement arguments) => Project.Resolve(arguments.Text("path"));

    protected abstract string Run(JsonElement arguments, string path);
}
