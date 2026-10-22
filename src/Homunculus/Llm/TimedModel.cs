namespace Homunculus.Llm;

/// <summary>
/// Passes every call to another model and records how long it took, under the given source name.
/// A failed or cancelled call still took time, so it is recorded too.
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
        try
        {
            return await inner.CompleteAsync(conversation, tools, cancellation);
        }
        finally
        {
            ledger.Record(source, _clock.GetElapsedTime(started));
        }
    }
}
