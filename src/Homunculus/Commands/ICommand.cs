namespace Homunculus.Commands;

/// <summary>A slash command the user can type, such as /help.</summary>
public interface ICommand
{
    string Name { get; }

    /// <summary>What may follow the name, for the help text; empty when nothing does.</summary>
    string Arguments { get; }

    string Description { get; }

    CommandResult Execute(string argument);
}
