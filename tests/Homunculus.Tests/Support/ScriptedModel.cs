using Homunculus.Llm;

namespace Homunculus.Tests.Support;

/// <summary>A language model that answers from a script and remembers what it was asked.</summary>
public sealed class ScriptedModel : ILanguageModel
{
    private readonly Queue<Message> _script;
    private readonly Message? _whenScriptEnds;

    public ScriptedModel(params Message[] script) => _script = new Queue<Message>(script);

    private ScriptedModel(Message repeated)
    {
        _script = new Queue<Message>();
        _whenScriptEnds = repeated;
    }

    /// <summary>A model that gives the same answer forever.</summary>
    public static ScriptedModel Repeating(Message reply) => new(reply);

    public List<IReadOnlyList<Message>> Conversations { get; } = [];
    public List<IReadOnlyList<ToolSpec>> ToolLists { get; } = [];
    public int Calls => Conversations.Count;
    public IReadOnlyList<Message> LastConversation => Conversations[^1];

    public Task<Message> CompleteAsync(
        IConversation conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Conversations.Add(conversation.Messages.ToList());
        ToolLists.Add(tools);

        if (_script.Count > 0) return Task.FromResult(_script.Dequeue());
        return Task.FromResult(_whenScriptEnds ?? throw new InvalidOperationException("The script has no more replies."));
    }
}
