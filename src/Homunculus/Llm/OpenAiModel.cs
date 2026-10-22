using System.Net.Http.Headers;
using System.Text;

namespace Homunculus.Llm;

/// <summary>Talks to any OpenAI-compatible /chat/completions endpoint.</summary>
public sealed class OpenAiModel(HttpClient http, string model) : ILanguageModel
{
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

        string body;
        try
        {
            using var content = new StringContent(request, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("chat/completions", content, cancellation);
            body = await response.Content.ReadAsStringAsync(cancellation);

            if (!response.IsSuccessStatusCode)
                throw new ModelException($"Model call failed ({(int)response.StatusCode}): {body}");
        }
        catch (HttpRequestException e)
        {
            throw new ModelException($"Could not reach the model: {e.Message}", e);
        }

        try
        {
            return OpenAiJson.ParseReply(body);
        }
        catch (FormatException e)
        {
            throw new ModelException($"Unexpected model response ({e.Message}): {body}", e);
        }
    }
}
