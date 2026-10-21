using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Llm;

public enum Role { System, User, Assistant, Tool }

public sealed record ToolCall(string Id, string Name, JsonElement Arguments);

public sealed record ToolSpec(string Name, string Description, JsonObject Parameters);

public sealed record Message(
    Role Role,
    string? Content = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    public static Message System(string content) => new(Role.System, content);
    public static Message User(string content) => new(Role.User, content);
    public static Message Assistant(string? content, IReadOnlyList<ToolCall>? toolCalls = null) => new(Role.Assistant, content, toolCalls);
    public static Message ToolResult(string callId, string content) => new(Role.Tool, content, ToolCallId: callId);
}
