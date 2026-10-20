using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Plans, and reads or writes inside the project, never need approval.</summary>
public sealed class ProjectBoundary(ProjectDirectory project)
{
    public bool IsSafe(ITool tool, JsonElement arguments) => tool.Access switch
    {
        ToolAccess.Plan => true,
        ToolAccess.Read or ToolAccess.Write => project.Contains(tool.TargetPath(arguments)!),
        _ => false,
    };
}
