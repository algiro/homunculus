using Homunculus.Llm;

namespace Homunculus.Tests.Support;

/// <summary>Builds the assistant messages a model could answer with.</summary>
public static class Reply
{
    public static Message Text(string content) => new(Role.Assistant, content);

    public static Message Tool(string name, string argumentsJson = "{}", string id = "call-1") =>
        Calls(new ToolCall(id, name, Json.Parse(argumentsJson)));

    public static Message Calls(params ToolCall[] calls) => new(Role.Assistant, null, calls);
}
