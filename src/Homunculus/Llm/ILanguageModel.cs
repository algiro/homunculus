namespace Homunculus.Llm;

public interface ILanguageModel
{
    Task<Message> CompleteAsync(
        IConversation conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation);
}
