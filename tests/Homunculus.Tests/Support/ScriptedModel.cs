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

    /// <summary>The token usage every reply reports; null means the server reports none.</summary>
    public TokenUsage? Usage { get; set; }

    /// <summary>The reasoning every reply carries; null means the model reports none.</summary>
    public string? Reasoning { get; set; }

    public List<IReadOnlyList<Message>> Conversations { get; } = [];
    public List<IReadOnlyList<ToolSpec>> ToolLists { get; } = [];
    public int Calls => Conversations.Count;
    public IReadOnlyList<Message> LastConversation => Conversations[^1];

    public Task<Reply> CompleteAsync(
        IConversation conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        Conversations.Add(conversation.Messages.ToList());
        ToolLists.Add(tools);

        var next = _script.Count > 0 ? _script.Dequeue() : _whenScriptEnds ?? throw new InvalidOperationException("The script has no more replies.");
        return Task.FromResult(new Reply(next, Usage, Reasoning));
    }
}
