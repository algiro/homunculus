using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>
/// Turns the policy's verdict into a decision, asking the user when the policy is unsure or says no: a judge model can be
/// wrong, so a denial is put to the user too, who may overrule it. Given the project, any file
/// access outside it is always put to the user first, whatever the policy mode and whatever was approved before; so is
/// deleting a file the agent did not create itself in this session.
/// </summary>
public sealed class Approvals(IPolicy policy, IUserInterface ui, ProjectDirectory? project = null, ChangedFiles? changes = null)
{
    private readonly HashSet<string> _alwaysAllowed = [];

    /// <summary>Returns null when the call may run, otherwise the reason it must not.</summary>
    public async Task<string?> CheckAsync(ITool tool, JsonElement arguments, CancellationToken cancellation)
    {
        var effect = tool.EffectOf(arguments);
        if (project is not null && project.IsOutside(effect))
            return ConfirmOutside(tool, arguments, project);

        if (effect is { Access: ToolAccess.Delete, Path: { } doomed } && changes?.WasCreatedInSession(doomed) != true)
            return ConfirmDelete(tool, arguments, doomed);

        if (_alwaysAllowed.Contains(tool.Name))
            return null;

        return await policy.EvaluateAsync(tool, arguments, cancellation) switch
        {
            Verdict.Allow => null,
            Verdict.Deny => ConfirmDenied(tool, arguments),
            _ => AskUser(tool, arguments),
        };
    }

    /// <summary>Never remembered and never decided by a policy: each access outside the project is the user's call.</summary>
    private string? ConfirmOutside(ITool tool, JsonElement arguments, ProjectDirectory project) =>
        ui.Choose($"{tool.Name} {arguments.GetRawText()} is OUTSIDE the project ({project.Root}). Allow?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    /// <summary>Deleting a file the agent did not make is the user's call, in every mode, and never remembered.</summary>
    private string? ConfirmDelete(ITool tool, JsonElement arguments, string path) =>
        ui.Choose($"{tool.Name} {arguments.GetRawText()} would delete {(project is null ? path : Path.GetRelativePath(project.Root, path))}, which you did not create in this session. Allow?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    /// <summary>Never remembered: overruling the policy once does not overrule it for the next call.</summary>
    private string? ConfirmDenied(ITool tool, JsonElement arguments) =>
        ui.Choose($"The policy would deny {tool.Name} {arguments.GetRawText()}. Run it anyway?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    private string? AskUser(ITool tool, JsonElement arguments)
    {
        var choice = ui.Choose($"Allow {tool.Name} {arguments.GetRawText()}?", "yna");
        if (choice == 'a') _alwaysAllowed.Add(tool.Name);
        return choice == 'n' ? "Refused by the user." : null;
    }
}
