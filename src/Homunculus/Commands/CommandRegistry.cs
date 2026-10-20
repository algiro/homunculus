namespace Homunculus.Commands;

public sealed class CommandRegistry
{
    private readonly List<ICommand> _commands = [];

    public IReadOnlyList<ICommand> All => _commands;

    public CommandRegistry Add(ICommand command)
    {
        _commands.Add(command);
        return this;
    }

    public ICommand? Find(string name) => _commands.FirstOrDefault(command => command.Name == name);
}
