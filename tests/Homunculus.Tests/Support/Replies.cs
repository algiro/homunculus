using Homunculus.Llm;

namespace Homunculus.Tests.Support;

/// <summary>Builds the assistant messages a model could answer with.</summary>
public static class Replies
{
    public static Message Text(string content) => Message.Assistant(content);

    /// <summary>A reply that ran out of room before it finished; <see cref="ScriptedModel"/> marks it cut off.</summary>
    public static readonly Message CutOff = Message.Assistant("half an ans");

    public static Message Tool(string name, string argumentsJson = "{}", string id = "call-1") =>
        Calls(new ToolCall(id, name, Json.Parse(argumentsJson)));

    public static Message Calls(params ToolCall[] calls) => Message.Assistant(null, calls);
}
