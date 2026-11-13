using System.Net;
using Homunculus.Tests.Llm;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class WebSearchToolTests
{
    private sealed class FakeProvider(params SearchResult[] results) : ISearchProvider
    {
        public string? Query { get; private set; }

        public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int count, CancellationToken cancellation)
        {
            Query = query;
            return Task.FromResult<IReadOnlyList<SearchResult>>(results);
        }
    }

    private static readonly SearchResult[] Hits =
    [
        new("Docs", "https://learn.microsoft.com/dotnet", "The docs."),
        new("Repo", "https://github.com/dotnet/runtime", "The runtime."),
        new("Wiki", "https://docs.github.com/en", "GitHub docs."),
    ];

    private static Task<string> Run(ISearchProvider provider, string json) =>
        new WebSearchTool(provider).RunAsync(Json.Parse(json), CancellationToken.None);

    [Test]
    public async Task ListsResultsAndEndsWithSources()
    {
        var provider = new FakeProvider(Hits);

        var text = await Run(provider, """{"query":"dotnet runtime"}""");

        Assert.That(provider.Query, Is.EqualTo("dotnet runtime"));
        Assert.That(text, Does.StartWith("1. Docs\n   https://learn.microsoft.com/dotnet\n   The docs."));
        Assert.That(text, Does.Contain("Sources:\n- [Docs](https://learn.microsoft.com/dotnet)"));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Test]
    public async Task GivesTodaysDateAndAsksToReadAPageBeforeAnswering()
    {
        var tool = new WebSearchTool(new FakeProvider(Hits), new FixedClock(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero)));

        var text = await tool.RunAsync(Json.Parse("""{"query":"weather"}"""), CancellationToken.None);

        Assert.That(text, Does.Contain("Today is 2026-11-13.").And.Contain("web_fetch"));
        Assert.That(tool.Description, Does.Contain("Today is 2026-11-13"));
    }

    [Test]
    public async Task KeepsOnlyAllowedDomainsAndTheirSubdomains()
    {
        var text = await Run(new FakeProvider(Hits), """{"query":"dotnet","allowed_domains":"github.com"}""");

        Assert.That(text, Does.Contain("github.com/dotnet/runtime").And.Contain("docs.github.com"));
        Assert.That(text, Does.Not.Contain("microsoft.com"));
    }

    [Test]
    public async Task DropsBlockedDomains()
    {
        var text = await Run(new FakeProvider(Hits), """{"query":"dotnet","blocked_domains":"github.com"}""");

        Assert.That(text, Does.Contain("microsoft.com"));
        Assert.That(text, Does.Not.Contain("github.com"));
    }

    [Test]
    public void RefusesAllowedAndBlockedTogether() =>
        Assert.ThrowsAsync<ToolException>(() =>
            Run(new FakeProvider(Hits), """{"query":"dotnet","allowed_domains":"a.com","blocked_domains":"b.com"}"""));

    [Test]
    public void RefusesAnEmptyQuery() =>
        Assert.ThrowsAsync<ToolException>(() => Run(new FakeProvider(Hits), """{"query":" "}"""));

    [Test]
    public async Task SaysSoWhenNothingMatches() =>
        Assert.That(await Run(new FakeProvider(), """{"query":"nothing here"}"""), Is.EqualTo("No results for 'nothing here'."));

    [Test]
    public void NeedsPermissionBecauseItReachesTheNetwork() =>
        Assert.That(new WebSearchTool(new FakeProvider()).EffectOf(Json.Parse("{}")).Access, Is.EqualTo(ToolAccess.Network));

    [Test]
    public async Task BraveProviderSendsTheKeyAndReadsTheResults()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """
            {"web":{"results":[{"title":"Docs","url":"https://example.com","description":"Hello"},{"title":"No link"}]}}
            """);

        var results = await new BraveSearchProvider(new HttpClient(handler), "secret").SearchAsync("a b", 5, CancellationToken.None);

        Assert.That(results, Is.EqualTo(new[] { new SearchResult("Docs", "https://example.com", "Hello") }));
        Assert.That(handler.Request!.RequestUri!.Query, Is.EqualTo("?q=a%20b&count=5"));
        Assert.That(handler.Request.Headers.GetValues("X-Subscription-Token"), Is.EqualTo(new[] { "secret" }));
    }

    [Test]
    public void DuckDuckGoPageIsReadIntoResults()
    {
        const string html = """
            <a rel="nofollow" class="result__a" href="https://example.com/a">First &amp; best</a>
            <a class="result__snippet" href="https://example.com/a">Some <b>bold</b> text</a>
            <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.org%2Fb&amp;rut=x">Second</a>
            <a class="result__snippet" href="x">More</a>
            <a rel="nofollow" class="result__a" href="https://duckduckgo.com/y.js?ad=1">An advert</a>
            """;

        Assert.That(DuckDuckGoSearchProvider.Parse(html), Is.EqualTo(new[]
        {
            new SearchResult("First & best", "https://example.com/a", "Some bold text"),
            new SearchResult("Second", "https://example.org/b", "More"),
        }));
    }

    [Test]
    public void DuckDuckGoChallengePageIsReportedNotTreatedAsNoResults() =>
        Assert.ThrowsAsync<ToolException>(() =>
            new DuckDuckGoSearchProvider(new HttpClient(new StubHttpHandler(HttpStatusCode.Accepted, "<html>challenge</html>")))
                .SearchAsync("x", 5, CancellationToken.None));

    [Test]
    public void BraveProviderReportsAFailedRequest() =>
        Assert.ThrowsAsync<ToolException>(() =>
            new BraveSearchProvider(new HttpClient(new StubHttpHandler(HttpStatusCode.Unauthorized, "{}")), "bad")
                .SearchAsync("x", 5, CancellationToken.None));
}
