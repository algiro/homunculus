namespace Homunculus.Commands;

public sealed class ExitCommand : ICommand
{
    public string Name => "/exit";
    public string Arguments => "";
    public string Description => "quit";

    public CommandResult Execute(string argument) => CommandResult.Exit;
}
