using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Llm;

/// <summary>Converts between our messages and the OpenAI chat-completions JSON.</summary>
public static class OpenAiJson
{
    public static JsonObject Request(string model, IReadOnlyList<Message> conversation, IReadOnlyList<ToolSpec> tools)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(conversation.Select(ToJson).ToArray()),
        };
        if (tools.Count > 0)
            request["tools"] = new JsonArray(tools.Select(ToJson).ToArray());
        return request;
    }

    /// <exception cref="FormatException">The body is not a chat-completions reply.</exception>
    public static Message Reply(string body)
    {
        try
        {
            var message = JsonNode.Parse(body)?["choices"]?.AsArray().FirstOrDefault()?["message"]
                ?? throw new FormatException("no choices[0].message");
            var calls = message["tool_calls"]?.AsArray().Select(ToolCallFromJson).ToList();
            return new Message(Role.Assistant, message["content"]?.GetValue<string>(), calls);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            throw new FormatException(e.Message, e);
        }
    }

    private static JsonNode ToJson(Message message)
    {
        var json = new JsonObject
        {
            ["role"] = message.Role.ToString().ToLowerInvariant(),
            ["content"] = message.Content,
        };
        if (message.ToolCallId is not null)
            json["tool_call_id"] = message.ToolCallId;
        if (message.HasToolCalls)
            json["tool_calls"] = new JsonArray(message.ToolCalls!.Select(ToJson).ToArray());
        return json;
    }

    private static JsonNode ToJson(ToolCall call) => new JsonObject
    {
        ["id"] = call.Id,
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = call.Name,
            ["arguments"] = call.Arguments.GetRawText(),
        },
    };

    private static JsonNode ToJson(ToolSpec spec) => new JsonObject
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = spec.Name,
            ["description"] = spec.Description,
            ["parameters"] = spec.Parameters.DeepClone(),
        },
    };

    private static ToolCall ToolCallFromJson(JsonNode? json)
    {
        var function = json?["function"] ?? throw new FormatException("tool call without function");
        var arguments = function["arguments"]?.GetValue<string>();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
        return new ToolCall(
            json["id"]?.GetValue<string>() ?? throw new FormatException("tool call without id"),
            function["name"]?.GetValue<string>() ?? throw new FormatException("tool call without name"),
            document.RootElement.Clone());
    }
}
