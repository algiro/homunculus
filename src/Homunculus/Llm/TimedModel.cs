namespace Homunculus.Llm;

/// <summary>
/// Passes every call to another model and records how long it took, under the given source name.
/// The reply carries the time too, so the caller can show it for each call. A failed or cancelled call still took time,
/// so it is recorded in the ledger as well.
/// </summary>
public sealed class TimedModel(ILanguageModel inner, TimeLedger ledger, string source, TimeProvider? clock = null) : ILanguageModel
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<Reply> CompleteAsync(
        IConversation conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation)
    {
        var started = _clock.GetTimestamp();
        Reply reply;
        try
        {
            reply = await inner.CompleteAsync(conversation, tools, cancellation);
        }
        catch
        {
            ledger.Record(source, _clock.GetElapsedTime(started));
            throw;
        }

        var elapsed = _clock.GetElapsedTime(started);
        ledger.Record(source, elapsed);
        return reply with { Elapsed = elapsed };
    }
}
