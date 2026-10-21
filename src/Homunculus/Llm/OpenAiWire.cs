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

    public sealed record ReplyDto(List<ChoiceDto>? Choices);

    public sealed record ChoiceDto(MessageDto? Message);
}

/// <summary>Maps our types to the wire shapes and back, so OpenAiJson only has to pick the options.</summary>
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

    extension(IConversation conversation)
    {
        public string ToRequestJson(string model, IReadOnlyList<ToolSpec> tools, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(new OpenAiWire.RequestDto(
                model,
                conversation.Messages.Select(m => m.ToWire()).ToList(),
                tools.Count > 0 ? tools.Select(t => t.ToWire()).ToList() : null), options);
    }

    extension(string body)
    {
        /// <exception cref="FormatException">The body is not a chat-completions reply.</exception>
        public Message ToReplyMessage(JsonSerializerOptions options)
        {
            try
            {
                var message = JsonSerializer.Deserialize<OpenAiWire.ReplyDto>(body, options)?.Choices?.FirstOrDefault()?.Message
                    ?? throw new FormatException("no choices[0].message");
                return Message.Assistant(message.Content, message.ToolCalls?.Select(c => c.FromWire()).ToList());
            }
            catch (JsonException e)
            {
                throw new FormatException(e.Message, e);
            }
        }
    }
}
