using System.Net;
using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>Searches with DuckDuckGo's plain HTML page, which needs no key. The page is scraped, so it can change without notice.</summary>
public sealed partial class DuckDuckGoSearchProvider(HttpClient http) : ISearchProvider
{
    public const string Endpoint = "https://html.duckduckgo.com/html/";

    [GeneratedRegex("""<a[^>]*class="result__a"[^>]*href="(?<url>[^"]*)"[^>]*>(?<title>.*?)</a>""", RegexOptions.Singleline)]
    private static partial Regex TitleLink();

    [GeneratedRegex("""<a[^>]*class="result__snippet"[^>]*>(?<snippet>.*?)</a>""", RegexOptions.Singleline)]
    private static partial Regex Snippet();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int count, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new FormUrlEncodedContent([KeyValuePair.Create("q", query)]),
        };
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Homunculus)");

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
            if (!response.IsSuccessStatusCode)
                throw new ToolException($"The search service answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            var results = Parse(await response.Content.ReadAsStringAsync(cancellation));
            if (results.Count == 0 && response.StatusCode != HttpStatusCode.OK)
                throw new ToolException(
                    "DuckDuckGo refuses automated searches (it answered with a challenge page, not results). " +
                    "Web search needs an API key: tell the user to set HOMUNCULUS_SEARCH_API_KEY (Brave Search). Do not retry.");
            return results.Take(count).ToList();
        }
    }

    public static IReadOnlyList<SearchResult> Parse(string html)
    {
        var links = TitleLink().Matches(html);
        var snippets = Snippet().Matches(html);
        var results = new List<SearchResult>();
        for (var i = 0; i < links.Count; i++)
        {
            var url = RealUrl(WebUtility.HtmlDecode(links[i].Groups["url"].Value));
            if (url is null) continue;
            var snippet = i < snippets.Count ? Text(snippets[i].Groups["snippet"].Value) : "";
            results.Add(new SearchResult(Text(links[i].Groups["title"].Value), url, snippet));
        }

        return results;
    }

    private static string Text(string html) => WebUtility.HtmlDecode(Tag().Replace(html, "")).Trim();

    /// <summary>Results are sometimes wrapped in a redirect whose 'uddg' parameter holds the real address; adverts are dropped.</summary>
    private static string? RealUrl(string href)
    {
        if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) return null;
        if (uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            if (uri.AbsolutePath != "/l/") return null;
            var target = uri.Query.TrimStart('?').Split('&')
                .Select(pair => pair.Split('=', 2))
                .FirstOrDefault(pair => pair[0] == "uddg" && pair.Length == 2);
            return target is null ? null : Uri.UnescapeDataString(target[1]);
        }

        return href;
    }
}
