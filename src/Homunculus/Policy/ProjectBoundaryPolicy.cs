using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Reads inside the project never need this policy's approval; everything else is up to the wrapped policy.</summary>
public sealed class ProjectBoundaryPolicy(ProjectDirectory project, IPolicy inner) : IPolicy
{
    public Task<Verdict> EvaluateAsync(ITool tool, JsonElement arguments, CancellationToken cancellation) =>
        IsSafe(tool.EffectOf(arguments))
            ? Task.FromResult(Verdict.Allow)
            : inner.EvaluateAsync(tool, arguments, cancellation);

    private bool IsSafe(ToolEffect effect) => effect switch
    {
        { Path: { } path } => project.Contains(path),
        _ => false,
    };
}
