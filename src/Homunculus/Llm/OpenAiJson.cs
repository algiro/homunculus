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

    public static string BuildRequest(string model, IConversation conversation, IReadOnlyList<ToolSpec> tools) =>
        JsonSerializer.Serialize(new OpenAiWire.RequestDto(
            model,
            conversation.Messages.Select(m => m.ToWire()).ToList(),
            tools.Count > 0 ? tools.Select(t => t.ToWire()).ToList() : null), Options);

    /// <exception cref="FormatException">The body is not a chat-completions reply.</exception>
    public static Reply ParseReply(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<OpenAiWire.ReplyDto>(body, Options)?.FromWire()
                ?? throw new FormatException("no reply");
        }
        catch (JsonException e)
        {
            throw new FormatException(e.Message, e);
        }
    }
}
