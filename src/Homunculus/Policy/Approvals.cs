using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>
/// Turns the policy's verdict into a decision, asking the user when the policy is unsure. Given the project, any file
/// access outside it is always put to the user first, whatever the policy mode and whatever was approved before.
/// </summary>
public sealed class Approvals(IPolicy policy, IUserInterface ui, ProjectDirectory? project = null)
{
    private readonly HashSet<string> _alwaysAllowed = [];

    /// <summary>Returns null when the call may run, otherwise the reason it must not.</summary>
    public async Task<string?> CheckAsync(ITool tool, JsonElement arguments, CancellationToken cancellation)
    {
        if (project is not null && project.IsOutside(tool.EffectOf(arguments)))
            return ConfirmOutside(tool, arguments, project);

        if (_alwaysAllowed.Contains(tool.Name))
            return null;

        return await policy.EvaluateAsync(tool, arguments, cancellation) switch
        {
            Verdict.Allow => null,
            Verdict.Deny => "Denied by the policy.",
            _ => AskUser(tool, arguments),
        };
    }

    /// <summary>Never remembered and never decided by a policy: each access outside the project is the user's call.</summary>
    private string? ConfirmOutside(ITool tool, JsonElement arguments, ProjectDirectory project) =>
        ui.Choose($"{tool.Name} {arguments.GetRawText()} is OUTSIDE the project ({project.Root}). Allow?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    private string? AskUser(ITool tool, JsonElement arguments)
    {
        var choice = ui.Choose($"Allow {tool.Name} {arguments.GetRawText()}?", "yna");
        if (choice == 'a') _alwaysAllowed.Add(tool.Name);
        return choice == 'n' ? "Refused by the user." : null;
    }
}
