using System.Text.Json;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>
/// Turns the policy's verdict into a decision, asking the user when the policy is unsure or says no: a judge model can be
/// wrong, so a denial is put to the user too, who may overrule it. Two kinds of call are always put to
/// the user first, whatever the policy mode and whatever was approved before, and each one needs its own yes: any file access
/// outside the project, and a command that starts a program which is not on the allowed list. An "always" answer for a command
/// covers that exact command only (the same program with the same arguments); for any other tool it covers the tool.
/// </summary>
public sealed class Approvals(IPolicy policy, IUserInterface ui, ProjectDirectory? project = null, AllowedPrograms? programs = null)
{
    private readonly HashSet<string> _alwaysAllowed = [];

    /// <summary>Returns null when the call may run, otherwise the reason it must not.</summary>
    /// <exception cref="ToolException">The arguments are not ones the tool can describe.</exception>
    public async Task<string?> CheckAsync(ITool tool, JsonElement arguments, CancellationToken cancellation)
    {
        var effect = tool.EffectOf(arguments);
        if (project is not null && project.IsOutside(effect))
            return ConfirmOutside(tool, arguments, project);

        if (programs is not null && effect is { Access: ToolAccess.Command, Program: { } program } && !programs.Contains(program))
            return ConfirmProgram(tool, arguments, program, programs);

        var grant = GrantFor(tool, effect);
        if (_alwaysAllowed.Contains(grant))
            return null;

        return await policy.EvaluateAsync(tool, arguments, cancellation) switch
        {
            Verdict.Allow => null,
            Verdict.Deny => ConfirmDenied(tool, arguments),
            _ => AskUser(tool, arguments, grant, effect.Access == ToolAccess.Command),
        };
    }

    /// <summary>What an "always" answer remembers: the exact command for a command, the tool for anything else.</summary>
    private static string GrantFor(ITool tool, ToolEffect effect) =>
        effect is { Access: ToolAccess.Command, Command: { } command } ? $"{tool.Name} {command}" : tool.Name;

    /// <summary>Never remembered and never decided by a policy: each access outside the project is the user's call.</summary>
    private string? ConfirmOutside(ITool tool, JsonElement arguments, ProjectDirectory project) =>
        ui.Choose($"{tool.Name} {arguments.GetRawText()} is OUTSIDE the project ({project.Root}). Allow?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    /// <summary>Never remembered and never decided by a policy: each run of a program off the list is the user's call.</summary>
    private string? ConfirmProgram(ITool tool, JsonElement arguments, string program, AllowedPrograms programs) =>
        ui.Choose($"{tool.Name} {arguments.GetRawText()} runs '{program}', which is NOT an allowed program ({programs.Describe()}). Allow?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    /// <summary>Never remembered: overruling the policy once does not overrule it for the next call.</summary>
    private string? ConfirmDenied(ITool tool, JsonElement arguments) =>
        ui.Choose($"The policy would deny {tool.Name} {arguments.GetRawText()}. Run it anyway?", "yn") == 'y'
            ? null
            : "Refused by the user.";

    private string? AskUser(ITool tool, JsonElement arguments, string grant, bool isCommand)
    {
        var always = isCommand ? " (a = always allow exactly this command)" : "";
        var choice = ui.Choose($"Allow {tool.Name} {arguments.GetRawText()}?{always}", "yna");
        if (choice == 'a') _alwaysAllowed.Add(grant);
        return choice == 'n' ? "Refused by the user." : null;
    }
}
