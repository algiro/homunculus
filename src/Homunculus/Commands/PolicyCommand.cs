using Homunculus.Policy;

namespace Homunculus.Commands;

public sealed class PolicyCommand(SwitchablePolicy policy, IUserInterface ui) : ICommand
{
    public string Name => "/policy";
    public string Arguments => "[mode]";
    public string Description => "show or set the approval mode";

    public CommandResult Execute(string argument)
    {
        var modes = string.Join(", ", policy.Modes);
        if (argument.Length > 0 && !policy.TrySetMode(argument))
            ui.Show($"Unknown mode. Choose one of: {modes}.");
        else
            ui.Show($"Policy: {policy.Mode} (modes: {modes})");
        return CommandResult.Continue;
    }
}
