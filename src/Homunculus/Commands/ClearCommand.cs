namespace Homunculus.Commands;

public sealed class ClearCommand(Agent agent, IUserInterface ui) : ICommand
{
    public string Name => "/clear";
    public string Arguments => "";
    public string Description => "forget the conversation";

    public CommandResult Execute(string argument)
    {
        agent.ClearConversation();
        ui.Show("Conversation cleared.");
        return CommandResult.Continue;
    }
}
