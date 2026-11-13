using System.Text.Json;

namespace Homunculus.Tools;

public enum ToolAccess { Plan, Read, Write, Delete, Command, Network }

/// <summary>
/// What one tool call does to the outside world: the kind of access and, for files, which one. For a command,
/// <paramref name="Program"/> is the program it starts and <paramref name="Command"/> the program with its arguments, written
/// so that two calls have the same value only when they run exactly the same thing.
/// </summary>
public readonly record struct ToolEffect(ToolAccess Access, string? Path = null, string? Program = null, string? Command = null)
{
    public static ToolEffect Planning { get; } = new(ToolAccess.Plan);
    public static ToolEffect Running(string program, IReadOnlyList<string> arguments) =>
        new(ToolAccess.Command, Program: program, Command: JsonSerializer.Serialize<IEnumerable<string>>([program, .. arguments]));

    public static ToolEffect Searching { get; } = new(ToolAccess.Network);
}
