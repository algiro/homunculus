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

    public static string Request(string model, IConversation conversation, IReadOnlyList<ToolSpec> tools) =>
        JsonSerializer.Serialize(new OpenAiWire.RequestDto(
            model,
            conversation.Messages.Select(m => m.ToWire()).ToList(),
            tools.Count > 0 ? tools.Select(t => t.ToWire()).ToList() : null), Options);

    /// <exception cref="FormatException">The body is not a chat-completions reply.</exception>
    public static Message Reply(string body)
    {
        try
        {
            var message = JsonSerializer.Deserialize<OpenAiWire.ReplyDto>(body, Options)?.Choices?.FirstOrDefault()?.Message
                ?? throw new FormatException("no choices[0].message");
            return Message.Assistant(message.Content, message.ToolCalls?.Select(c => c.FromWire()).ToList());
        }
        catch (JsonException e)
        {
            throw new FormatException(e.Message, e);
        }
    }
}
