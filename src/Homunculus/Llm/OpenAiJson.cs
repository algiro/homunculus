using System.Text.Json;
using System.Text.Json.Serialization;

namespace Homunculus.Llm;

/// <summary>Converts between our messages and the OpenAI chat-completions JSON.</summary>
public static class OpenAiJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Request(string model, IReadOnlyList<Message> conversation, IReadOnlyList<ToolSpec> tools) =>
        JsonSerializer.Serialize(new OpenAiWire.Request(
            model,
            conversation.Select(ToWire).ToList(),
            tools.Count > 0 ? tools.Select(ToWire).ToList() : null), Options);

    /// <exception cref="FormatException">The body is not a chat-completions reply.</exception>
    public static Message Reply(string body)
    {
        try
        {
            var message = JsonSerializer.Deserialize<OpenAiWire.Reply>(body, Options)?.Choices?.FirstOrDefault()?.Message
                ?? throw new FormatException("no choices[0].message");
            return new Message(Role.Assistant, message.Content, message.ToolCalls?.Select(FromWire).ToList());
        }
        catch (JsonException e)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static OpenAiWire.Message ToWire(Message message) => new(
        message.Role.ToString().ToLowerInvariant(),
        message.Content,
        message.ToolCalls?.Select(ToWire).ToList(),
        message.ToolCallId);

    private static OpenAiWire.ToolCall ToWire(ToolCall call) =>
        new(call.Id, "function", new OpenAiWire.FunctionCall(call.Name, call.Arguments.GetRawText()));

    private static OpenAiWire.Tool ToWire(ToolSpec spec) =>
        new("function", new OpenAiWire.FunctionSpec(spec.Name, spec.Description, spec.Parameters));

    private static ToolCall FromWire(OpenAiWire.ToolCall call)
    {
        var function = call.Function ?? throw new FormatException("tool call without function");
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(function.Arguments) ? "{}" : function.Arguments);
        return new ToolCall(
            call.Id ?? throw new FormatException("tool call without id"),
            function.Name ?? throw new FormatException("tool call without name"),
            document.RootElement.Clone());
    }
}
