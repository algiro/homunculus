using System.Net.Http.Headers;
using System.Text.Json;

namespace Homunculus.Tools;

/// <summary>Searches with the Brave Search API (https://api.search.brave.com), which needs an API key.</summary>
public sealed class BraveSearchProvider(HttpClient http, string apiKey) : ISearchProvider
{
    public const string Endpoint = "https://api.search.brave.com/res/v1/web/search";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int count, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?q={Uri.EscapeDataString(query)}&count={count}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Subscription-Token", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellation);
        }
        catch (HttpRequestException e)
        {
            throw new ToolException($"The search service could not be reached: {e.Message}");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellation);
            if (!response.IsSuccessStatusCode)
                throw new ToolException($"The search service answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            return Parse(body);
        }
    }

    public static IReadOnlyList<SearchResult> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("web", out var web)
                || !web.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                return [];

            return results.EnumerateArray()
                .Select(item => new SearchResult(
                    item.OptionalText("title") ?? "",
                    item.OptionalText("url") ?? "",
                    item.OptionalText("description") ?? ""))
                .Where(result => result.Url.Length > 0)
                .ToList();
        }
        catch (JsonException)
        {
            throw new ToolException("The search service sent an answer that is not JSON.");
        }
    }
}
