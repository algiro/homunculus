using Homunculus.Tools;

namespace Homunculus.Commands;

public sealed class ToolsCommand(ToolRegistry tools, IUserInterface ui) : ICommand
{
    public string Name => "/tools";
    public string Arguments => "";
    public string Description => "list the tools";

    public CommandResult Execute(string argument)
    {
        ui.Show(string.Join('\n', tools.All.Select(tool => $"{tool.Name} - {tool.Description}")));
        return CommandResult.Continue;
    }
}
