namespace Homunculus.Llm;

/// <summary>The shapes of the OpenAI chat-completions JSON. Property names become snake_case when serialized.</summary>
internal static class OpenAiWire
{
    public sealed record RequestDto(string Model, List<MessageDto> Messages, string? ReasoningEffort = null);

    public sealed record MessageDto(string Role, string? Content);

    public sealed record ReplyDto(List<ChoiceDto>? Choices, UsageDto? Usage);

    public sealed record ChoiceDto(ReplyMessageDto? Message, string? FinishReason = null);

    /// <summary>The assistant's message as a server answers with it; the thinking of a reasoning model arrives as <c>reasoning</c> or <c>reasoning_content</c>.</summary>
    public sealed record ReplyMessageDto(
        string? Content,
        string? Reasoning = null,
        string? ReasoningContent = null);

    public sealed record UsageDto(int? PromptTokens, PromptTokensDetailsDto? PromptTokensDetails, int? CompletionTokens, int? TotalTokens);

    public sealed record PromptTokensDetailsDto(int? CachedTokens);
}

/// <summary>Maps our message types to the wire shapes and back.</summary>
internal static class OpenAiWireMapping
{
    extension(Message message)
    {
        public OpenAiWire.MessageDto ToWire() => new(message.Role.ToString().ToLowerInvariant(), message.Content);
    }

    extension(OpenAiWire.UsageDto usage)
    {
        /// <summary>Missing counts become zero; a missing total is the sum of input and output.</summary>
        public TokenUsage FromWire()
        {
            var input = usage.PromptTokens ?? 0;
            var output = usage.CompletionTokens ?? 0;
            return new TokenUsage(input, output, usage.TotalTokens ?? input + output, usage.PromptTokensDetails?.CachedTokens ?? 0);
        }
    }

    extension(OpenAiWire.ReplyDto reply)
    {
        /// <summary>A reply cut off by the length limit keeps its text and says it was cut off.</summary>
        /// <exception cref="FormatException">The reply has no first choice with a message.</exception>
        public Reply FromWire()
        {
            var choice = reply.Choices?.FirstOrDefault();
            var message = choice?.Message ?? throw new FormatException("no choices[0].message");
            var reasoning = message.Reasoning ?? message.ReasoningContent;
            return new Reply(
                Message.Assistant(message.Content),
                reply.Usage?.FromWire(),
                string.IsNullOrWhiteSpace(reasoning) ? null : reasoning.Trim(),
                CutOff: choice.FinishReason == "length");
        }
    }
}
