using Homunculus.Llm;

namespace Homunculus.Tests.Support;

/// <summary>A conversation made of exactly the given messages, with no system prompt added.</summary>
public sealed class FixedConversation(IReadOnlyList<Message> messages) : IConversation
{
    public IReadOnlyList<Message> Messages => messages;

    public void Add(Message message) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();
}
