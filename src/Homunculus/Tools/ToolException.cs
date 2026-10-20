namespace Homunculus.Tools;

/// <summary>A tool call failed in a way the model can understand and correct.</summary>
public sealed class ToolException(string message) : Exception(message);
