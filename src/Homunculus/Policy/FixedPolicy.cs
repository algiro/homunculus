using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Always gives the same verdict: Allow for "allow-all", Ask for "effect".</summary>
public sealed class FixedPolicy(Verdict verdict) : IPolicy
{
    public Task<Verdict> EvaluateAsync(ITool tool, JsonElement arguments, CancellationToken cancellation) =>
        Task.FromResult(verdict);
}
