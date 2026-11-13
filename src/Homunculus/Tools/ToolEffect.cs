namespace Homunculus.Tools;

public enum ToolAccess { Read, Write }

/// <summary>What one tool call does to the outside world: the kind of access and, for files, which one.</summary>
public readonly record struct ToolEffect(ToolAccess Access, string? Path = null);
