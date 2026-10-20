using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Llm;

/// <summary>Talks to any OpenAI-compatible /chat/completions endpoint.</summary>
public sealed class OpenAiModel : ILanguageModel
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OpenAiModel(HttpClient http, string baseUrl, string apiKey, string model)
    {
        _http = http;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.Timeout = TimeSpan.FromMinutes(5);
        _model = model;
    }

    public async Task<Message> CompleteAsync(
        IReadOnlyList<Message> conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation)
    {
        var request = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray(conversation.Select(ToJson).ToArray()),
        };
        if (tools.Count > 0)
            request["tools"] = new JsonArray(tools.Select(ToJson).ToArray());

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("chat/completions", content, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Model call failed ({(int)response.StatusCode}): {body}");

        return FromJson(JsonNode.Parse(body)!["choices"]![0]!["message"]!);
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

    private static Message FromJson(JsonNode json)
    {
        var calls = json["tool_calls"]?.AsArray().Select(ToolCallFromJson).ToList();
        return new Message(Role.Assistant, json["content"]?.GetValue<string>(), calls);
    }

    private static ToolCall ToolCallFromJson(JsonNode? json)
    {
        var function = json!["function"]!;
        var arguments = function["arguments"]?.GetValue<string>();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
        return new ToolCall(json["id"]!.GetValue<string>(), function["name"]!.GetValue<string>(), document.RootElement.Clone());
    }
}
