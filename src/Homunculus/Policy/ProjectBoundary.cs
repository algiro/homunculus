using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Plans, and reads or writes inside the project, never need approval.</summary>
public sealed class ProjectBoundary(ProjectDirectory project)
{
    public bool IsSafe(ITool tool, JsonElement arguments) => tool.EffectOf(arguments) switch
    {
        { Access: ToolAccess.Plan } => true,
        { Access: ToolAccess.Read or ToolAccess.Write, Path: { } path } => project.Contains(path),
        _ => false,
    };
}
