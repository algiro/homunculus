using Homunculus.Llm;

namespace Homunculus.Tests.Support;

/// <summary>Builds the assistant messages a model could answer with.</summary>
public static class Replies
{
    public static Message Text(string content) => Message.Assistant(content);

    /// <summary>A reply that ran out of room before it finished; <see cref="ScriptedModel"/> marks it cut off.</summary>
    public static readonly Message CutOff = Message.Assistant("half an ans");

}
