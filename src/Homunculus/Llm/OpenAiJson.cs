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
        conversation.ToRequestJson(model, tools, Options);

    /// <exception cref="FormatException">The body is not a chat-completions reply.</exception>
    public static Message Reply(string body) => body.ToReplyMessage(Options);
}
