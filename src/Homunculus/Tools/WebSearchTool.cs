using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

/// <summary>
/// Searches the web, shaped like Claude Code's WebSearch tool: a query, optional domains to keep or drop,
/// and results as titles, links and snippets ending in a list of sources.
/// </summary>
public sealed class WebSearchTool(ISearchProvider provider, TimeProvider? clock = null) : ITool
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public const string ToolName = "web_search";

    /// <summary>Asks for more than shown so that dropping blocked domains still leaves enough.</summary>
    private const int Fetched = 20;

    public string Name => ToolName;

    public string Description =>
        $"Search the web for anything you cannot know from the project: today's weather, news, prices, sports, facts about people or places, documentation, error messages (at most {Limits.WebResults} results). " +
        $"Returns titles, links and snippets. Today is {_clock.GetLocalNow():yyyy-MM-dd}; include it when you look for recent material. " +
        "Snippets are often not enough (for example for a forecast): read the best link with web_fetch, never with curl.";

    public JsonObject Parameters => Schema.Of(
        Parameter.Required("query", "What to search for."),
        Parameter.Optional("allowed_domains", "Only when the user asks for particular sites: comma-separated domains whose results are kept, for example 'learn.microsoft.com'. Leave it out otherwise, so that no good result is lost."),
        Parameter.Optional("blocked_domains", "Comma-separated domains whose results are dropped."));

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Searching;

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        var query = arguments.Text("query").Trim();
        if (query.Length < 2) throw new ToolException("The query needs at least 2 characters.");

        var allowed = Domains(arguments.OptionalText("allowed_domains"));
        var blocked = Domains(arguments.OptionalText("blocked_domains"));
        if (allowed.Count > 0 && blocked.Count > 0)
            throw new ToolException("Give allowed_domains or blocked_domains, not both.");

        var kept = (await provider.SearchAsync(query, Fetched, cancellation))
            .Where(result => allowed.Count == 0 || allowed.Any(domain => IsOn(result.Url, domain)))
            .Where(result => !blocked.Any(domain => IsOn(result.Url, domain)))
            .Take(Limits.WebResults)
            .ToList();

        if (kept.Count == 0) return $"No results for '{query}'.";

        var lines = new List<string>();
        foreach (var (result, index) in kept.Select((result, index) => (result, index)))
        {
            lines.Add($"{index + 1}. {result.Title}");
            lines.Add($"   {result.Url}");
            if (result.Snippet.Length > 0) lines.Add($"   {Shorten(result.Snippet)}");
        }

        lines.Add("");
        lines.Add("Sources:");
        lines.AddRange(kept.Select(result => $"- [{result.Title}]({result.Url})"));
        lines.Add("");
        lines.Add($"Today is {_clock.GetLocalNow():yyyy-MM-dd}. Snippets are short and often out of date, so they are not an answer by themselves: " +
                  "before you answer, read the most relevant link with web_fetch (try another link if it fails), and answer from that page.");
        return string.Join('\n', lines);
    }

    private static string Shorten(string snippet) =>
        snippet.Length > Limits.WebSnippetChars ? snippet[..Limits.WebSnippetChars] + "..." : snippet;

    private static List<string> Domains(string? list) =>
        (list ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(domain => domain.ToLowerInvariant())
        .ToList();

    /// <summary>True for the domain itself and any subdomain of it, so 'github.com' covers 'docs.github.com'.</summary>
    private static bool IsOn(string url, string domain) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
}
