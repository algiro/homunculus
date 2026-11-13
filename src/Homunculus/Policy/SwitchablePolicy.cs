using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Holds several named policies and applies the one currently selected; the first is the default.</summary>
public sealed class SwitchablePolicy(IReadOnlyDictionary<string, IPolicy> modes) : IPolicy
{
    public string Mode { get; private set; } = modes.Keys.First();

    public IEnumerable<string> Modes => modes.Keys;

    public bool TrySetMode(string mode)
    {
        if (!modes.ContainsKey(mode)) return false;
        Mode = mode;
        return true;
    }

    public Task<Verdict> EvaluateAsync(ITool tool, JsonElement arguments, CancellationToken cancellation) =>
        modes[Mode].EvaluateAsync(tool, arguments, cancellation);
}
