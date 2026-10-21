namespace Homunculus.Llm;

/// <summary>The messages exchanged so far, always starting with the system prompt.</summary>
public interface IConversation
{
    IReadOnlyList<Message> Messages { get; }
    void Add(Message message);
    void Clear();
}

public sealed class Conversation(string systemPrompt) : IConversation
{
    private readonly List<Message> _messages = [Message.System(systemPrompt)];

    public IReadOnlyList<Message> Messages => _messages;

    public void Add(Message message) => _messages.Add(message);

    public void Clear()
    {
        _messages.Clear();
        _messages.Add(Message.System(systemPrompt));
    }
}
