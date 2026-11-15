using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus.Commands;

public sealed class PolicyCommand(SwitchablePolicy policy, IUserInterface ui, AllowedPrograms? programs = null) : ICommand
{
    public string Name => "/policy";
    public string Arguments => "[mode]";
    public string Description => "show or set the approval mode";

    public const string AllowAll = "allow-all";

    private string AllowAllWarning =>
        $"Warning: in {AllowAll} mode, file tools inside the project and the allowed programs ({programs?.Describe() ?? "all"}) run without asking. " +
        "Any other program, and anything outside the project, still asks. An allowed program can still run code the model wrote " +
        "(a build file, a git alias), so use this mode only on a project and files you trust.";

    public CommandResult Execute(string argument)
    {
        var modes = string.Join(", ", policy.Modes);
        if (argument.Length > 0 && !policy.TrySetMode(argument))
        {
            ui.Show($"Unknown mode. Choose one of: {modes}.");
            return CommandResult.Continue;
        }

        ui.Show($"Policy: {policy.Mode} (modes: {modes})");
        if (argument.Length > 0 && policy.Mode == AllowAll)
            ui.Show(AllowAllWarning);
        return CommandResult.Continue;
    }
}
