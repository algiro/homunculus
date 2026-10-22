namespace Homunculus.Llm;

public interface ILanguageModel
{
    /// <exception cref="ModelException">The model could not give a usable answer.</exception>
    /// <exception cref="OperationCanceledException">The call was cancelled.</exception>
    Task<Reply> CompleteAsync(
        IConversation conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation);
}
