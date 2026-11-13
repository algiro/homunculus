namespace Homunculus.Llm;

/// <summary>Passes every call to another model and records the tokens it reports, under the given source name.</summary>
public sealed class MeteredModel(ILanguageModel inner, UsageLedger ledger, string source) : ILanguageModel
{
    public async Task<Reply> CompleteAsync(
        IConversation conversation,
        CancellationToken cancellation)
    {
        var reply = await inner.CompleteAsync(conversation, cancellation);
        if (reply.Usage is { } usage) ledger.Record(source, usage);
        return reply;
    }
}
