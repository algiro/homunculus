using Homunculus.Tools;

namespace Homunculus.Commands;

public sealed class ClearCommand(Agent agent, Plan plan, IUserInterface ui) : ICommand
{
    public string Name => "/clear";
    public string Arguments => "";
    public string Description => "forget the conversation";

    public CommandResult Execute(string argument)
    {
        agent.ClearConversation();
        plan.Clear();
        ui.Show("Conversation cleared.");
        return CommandResult.Continue;
    }
}
