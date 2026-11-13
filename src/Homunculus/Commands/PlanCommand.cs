using Homunculus.Tools;

namespace Homunculus.Commands;

public sealed class PlanCommand(Plan plan, IUserInterface ui) : ICommand
{
    public string Name => "/plan";
    public string Arguments => "";
    public string Description => "show the current plan";

    public CommandResult Execute(string argument)
    {
        ui.Show(plan.Steps.Count == 0 ? "No plan yet." : plan.ToString());
        return CommandResult.Continue;
    }
}
