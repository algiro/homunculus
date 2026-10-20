using System.Text.Json.Nodes;

namespace Homunculus.Llm;

/// <summary>The shapes of the OpenAI chat-completions JSON. Property names become snake_case when serialized.</summary>
internal static class OpenAiWire
{
    public sealed record Request(string Model, List<Message> Messages, List<Tool>? Tools);

    public sealed record Message(string Role, string? Content, List<ToolCall>? ToolCalls = null, string? ToolCallId = null);

    public sealed record ToolCall(string? Id, string? Type, FunctionCall? Function);

    public sealed record FunctionCall(string? Name, string? Arguments);

    public sealed record Tool(string Type, FunctionSpec Function);

    public sealed record FunctionSpec(string Name, string Description, JsonObject Parameters);

    public sealed record Reply(List<Choice>? Choices);

    public sealed record Choice(Message? Message);
}
