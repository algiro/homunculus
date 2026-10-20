using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

public enum Verdict { Allow, Ask, Deny }

public interface IPolicy
{
    Task<Verdict> EvaluateAsync(ITool tool, JsonElement arguments, CancellationToken cancellation);
}
