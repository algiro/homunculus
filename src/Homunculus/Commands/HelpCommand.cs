namespace Homunculus.Commands;

public sealed class HelpCommand(CommandRegistry commands, IUserInterface ui) : ICommand
{
    public string Name => "/help";
    public string Arguments => "";
    public string Description => "show this help";

    public CommandResult Execute(string argument)
    {
        var lines = commands.All.Select(command => Line($"{command.Name} {command.Arguments}".TrimEnd(), command.Description));
        ui.Show(string.Join('\n', lines.Append(Line("ctrl-c", "stop the current turn"))));
        return CommandResult.Continue;
    }

    private static string Line(string usage, string description) => $"{usage,-16} {description}";
}
