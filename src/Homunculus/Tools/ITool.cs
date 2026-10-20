using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public enum ToolAccess { Plan, Read, Write, Command }

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonObject Parameters { get; }
    ToolAccess Access { get; }

    /// <summary>The file this call touches, or null when it does not touch one.</summary>
    string? TargetPath(JsonElement arguments);

    Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation);
}
