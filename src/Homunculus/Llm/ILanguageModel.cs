namespace Homunculus.Llm;

public interface ILanguageModel
{
    Task<Message> CompleteAsync(
        IReadOnlyList<Message> conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation);
}
