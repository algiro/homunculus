using System.Net.Http.Headers;
using System.Text;
using Homunculus.Logging;

namespace Homunculus.Llm;

/// <summary>Talks to any OpenAI-compatible /chat/completions endpoint.</summary>
public sealed class OpenAiModel(HttpClient http, string model, IEventLog? log = null) : ILanguageModel
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;

    /// <summary>One client can serve several models; it knows the server and the key, not the model.</summary>
    public static HttpClient CreateClient(string baseUrl, string apiKey, HttpMessageHandler? handler = null) =>
        new(handler ?? new HttpClientHandler())
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            DefaultRequestHeaders = { Authorization = new AuthenticationHeaderValue("Bearer", apiKey) },
            Timeout = TimeSpan.FromMinutes(5),
        };

    public async Task<Reply> CompleteAsync(
        IConversation conversation,
        IReadOnlyList<ToolSpec> tools,
        CancellationToken cancellation)
    {
        var request = OpenAiJson.BuildRequest(model, conversation, tools);

        _log.Write("llm_request", new { model, messages = conversation.Messages.Count, tools = tools.Count, requestChars = request.Length });

        var started = System.Diagnostics.Stopwatch.StartNew();
        string body;
        try
        {
            using var content = new StringContent(request, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("chat/completions", content, cancellation);
            body = await response.Content.ReadAsStringAsync(cancellation);

            // The body is logged exactly as the server sent it, before any parsing can lose or reject part of it.
            _log.Write("llm_response", new { model, status = (int)response.StatusCode, ms = started.ElapsedMilliseconds, body = JsonlEventLog.Raw(body) });

            if (!response.IsSuccessStatusCode)
                throw new ModelException($"Model call failed ({(int)response.StatusCode}): {body}");
        }
        catch (HttpRequestException e)
        {
            _log.Write("llm_error", new { model, ms = started.ElapsedMilliseconds, error = e.Message });
            throw new ModelException($"Could not reach the model: {e.Message}", e);
        }

        try
        {
            return OpenAiJson.ParseReply(body);
        }
        catch (FormatException e)
        {
            _log.Write("llm_parse_error", new { model, error = e.Message });
            throw new ModelException($"Unexpected model response ({e.Message}): {body}", e);
        }
    }
}
