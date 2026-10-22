using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Llm;

/// <summary>The shapes of the OpenAI chat-completions JSON. Property names become snake_case when serialized.</summary>
internal static class OpenAiWire
{
    public sealed record RequestDto(string Model, List<MessageDto> Messages, List<ToolDto>? Tools);

    public sealed record MessageDto(string Role, string? Content, List<ToolCallDto>? ToolCalls = null, string? ToolCallId = null);

    public sealed record ToolCallDto(string? Id, string? Type, FunctionCallDto? Function);

    public sealed record FunctionCallDto(string? Name, string? Arguments);

    public sealed record ToolDto(string Type, FunctionSpecDto Function);

    public sealed record FunctionSpecDto(string Name, string Description, JsonObject Parameters);

    public sealed record ReplyDto(List<ChoiceDto>? Choices, UsageDto? Usage);

    public sealed record ChoiceDto(ReplyMessageDto? Message, string? FinishReason = null);

    /// <summary>The assistant's message as a server answers with it; the thinking of a reasoning model arrives as <c>reasoning</c> or <c>reasoning_content</c>.</summary>
    public sealed record ReplyMessageDto(
        string? Content,
        List<ToolCallDto>? ToolCalls = null,
        string? Reasoning = null,
        string? ReasoningContent = null);

    public sealed record UsageDto(int? PromptTokens, PromptTokensDetailsDto? PromptTokensDetails, int? CompletionTokens, int? TotalTokens);

    public sealed record PromptTokensDetailsDto(int? CachedTokens);
}

/// <summary>Maps our tool and message types to the wire shapes and back.</summary>
internal static class OpenAiWireMapping
{
    extension(ToolCall call)
    {
        public OpenAiWire.ToolCallDto ToWire() =>
            new(call.Id, "function", new OpenAiWire.FunctionCallDto(call.Name, call.Arguments.GetRawText()));
    }

    extension(ToolSpec spec)
    {
        public OpenAiWire.ToolDto ToWire() =>
            new("function", new OpenAiWire.FunctionSpecDto(spec.Name, spec.Description, spec.Parameters));
    }

    extension(Message message)
    {
        public OpenAiWire.MessageDto ToWire() => new(
            message.Role.ToString().ToLowerInvariant(),
            message.Content,
            message.ToolCalls?.Select(c => c.ToWire()).ToList(),
            message.ToolCallId);
    }

    extension(OpenAiWire.ToolCallDto call)
    {
        public ToolCall FromWire()
        {
            var function = call.Function ?? throw new FormatException("tool call without function");
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(function.Arguments) ? "{}" : function.Arguments);
            return new ToolCall(
                call.Id ?? throw new FormatException("tool call without id"),
                function.Name ?? throw new FormatException("tool call without name"),
                document.RootElement.Clone());
        }
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
        /// <exception cref="FormatException">The reply has no first choice with a message, or the model was cut off.</exception>
        public Reply FromWire()
        {
            var choice = reply.Choices?.FirstOrDefault();
            var message = choice?.Message ?? throw new FormatException("no choices[0].message");
            if (choice.FinishReason == "length")
                throw new FormatException("the reply was cut off by the output limit (finish_reason: length)");

            var reasoning = message.Reasoning ?? message.ReasoningContent;
            return new Reply(
                Message.Assistant(message.Content, message.ToolCalls?.Select(c => c.FromWire()).ToList()),
                reply.Usage?.FromWire(),
                string.IsNullOrWhiteSpace(reasoning) ? null : reasoning.Trim());
        }
    }
}
