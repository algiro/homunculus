using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>Turns the policy's verdict into a decision, asking the user when the policy is unsure.</summary>
public sealed class Approvals(IPolicy policy, IUserInterface ui)
{
    private readonly HashSet<string> _alwaysAllowed = [];

    /// <summary>Returns null when the call may run, otherwise the reason it must not.</summary>
    public async Task<string?> CheckAsync(ITool tool, JsonElement arguments, CancellationToken cancellation)
    {
        if (_alwaysAllowed.Contains(tool.Name))
            return null;

        return await policy.EvaluateAsync(tool, arguments, cancellation) switch
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
