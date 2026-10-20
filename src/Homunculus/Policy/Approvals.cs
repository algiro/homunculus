using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

public sealed class Approvals(
    ProjectBoundary boundary,
    IReadOnlyDictionary<string, IPolicy> policies,
    IUserInterface ui)
{
    private readonly HashSet<string> _alwaysAllowed = [];

    public string Mode { get; private set; } = policies.Keys.First();

    public IEnumerable<string> Modes => policies.Keys;

    public bool TrySetMode(string mode)
    {
        if (!policies.ContainsKey(mode)) return false;
        Mode = mode;
        return true;
    }

    /// <summary>Returns null when the call may run, otherwise the reason it must not.</summary>
    public async Task<string?> CheckAsync(ITool tool, JsonElement arguments, CancellationToken cancellation)
    {
        if (boundary.IsSafe(tool, arguments) || _alwaysAllowed.Contains(tool.Name))
            return null;

        return await policies[Mode].EvaluateAsync(tool, arguments, cancellation) switch
        {
            Verdict.Allow => null,
            Verdict.Deny => "Denied by the policy judge.",
            _ => AskUser(tool, arguments),
        };
    }

    private string? AskUser(ITool tool, JsonElement arguments)
    {
        var choice = ui.Choose($"Allow {tool.Name} {arguments.GetRawText()}?", "yna");
        if (choice == 'a') _alwaysAllowed.Add(tool.Name);
        return choice == 'n' ? "Refused by the user." : null;
    }
}
