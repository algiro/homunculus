namespace Homunculus.Tools;

public enum ToolAccess { Plan, Read, Write, Delete, Command, Network }

/// <summary>What one tool call does to the outside world: the kind of access and, for files, which one.</summary>
public readonly record struct ToolEffect(ToolAccess Access, string? Path = null)
{
    public static ToolEffect Planning { get; } = new(ToolAccess.Plan);
    public static ToolEffect Running { get; } = new(ToolAccess.Command);
    public static ToolEffect Searching { get; } = new(ToolAccess.Network);
}
