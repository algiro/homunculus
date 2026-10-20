using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Plans, and reads or writes inside the project, never need approval; everything else is up to the wrapped policy.</summary>
public sealed class ProjectBoundaryPolicy(ProjectDirectory project, IPolicy inner) : IPolicy
{
    public Task<Verdict> EvaluateAsync(ITool tool, JsonElement arguments, CancellationToken cancellation) =>
        IsSafe(tool.EffectOf(arguments))
            ? Task.FromResult(Verdict.Allow)
            : inner.EvaluateAsync(tool, arguments, cancellation);

    private bool IsSafe(ToolEffect effect) => effect switch
    {
        { Access: ToolAccess.Plan } => true,
        { Access: ToolAccess.Read or ToolAccess.Write, Path: { } path } => project.Contains(path),
        _ => false,
    };
}
