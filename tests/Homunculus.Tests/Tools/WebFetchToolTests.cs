using System.Net;
using Homunculus.Tests.Llm;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class WebFetchToolTests
{
    private static Task<string> Run(HttpMessageHandler handler, string json) =>
        new WebFetchTool(new HttpClient(handler)).RunAsync(Json.Parse(json), CancellationToken.None);

    private static StubHttpHandler Page(string body) => new(HttpStatusCode.OK, body);

    [Test]
    public void HtmlTextKeepsTheReadableTextOnly()
    {
        const string html = """
            <html><head><title>Forecast &amp; more</title><style>p {color:red}</style><script>var x = 1;</script></head>
            <body><nav>Home | About</nav><h1>Las Palmas</h1><p>Sunny,   23&deg;C</p><p>Wind 14 km/h</p>
            <!-- hidden --><footer>Copyright</footer></body></html>
            """;

        var text = HtmlText.From(html);

        Assert.That(text, Is.EqualTo("Forecast & more\n\nLas Palmas\n\nSunny, 23°C\n\nWind 14 km/h"));
    }

    [Test]
    public void HtmlTextKeepsEachTableRowOnOneLineWithImageNames()
    {
        const string html = """
            <table><thead><tr><th>Hour</th><th>Sky</th><th>Temp</th></tr></thead>
            <tbody><tr><td>18</td><td><img src="81.png" alt="Fog" /></td><td><div class="t">15</div></td></tr>
            <tr><td></td><td></td><td></td></tr>
            <tr><td>19</td><td><img alt="Mist" src="82.png"></td><td>
              14</td></tr></tbody></table>
            """;

        Assert.That(HtmlText.From(html), Is.EqualTo("Hour | Sky | Temp\n18 | Fog | 15\n19 | Mist | 14"));
    }

    [Test]
    public void HtmlTextDropsMenusOfLinks() =>
        Assert.That(
            HtmlText.From("""<ul><li><a href="/a">Home</a></li><li><a href="/b"><span>About</span></a></li></ul><p>Sunny</p><ul><li>Wind <a href="/w">14 km/h</a></li></ul>"""),
            Is.EqualTo("Sunny\n\nWind 14 km/h"));

    [Test]
    public void HtmlTextReadsOnlyTheMainContentWhenThePageMarksIt() =>
        Assert.That(
            HtmlText.From("<body><div>Cookies and banners</div><main><p>Sunny, 23°C</p></main><div>Related links</div></body>"),
            Is.EqualTo("Sunny, 23°C"));

    [Test]
    public async Task ReturnsThePageAsText() =>
        Assert.That(await Run(Page("<html><body><p>Hello <b>world</b></p></body></html>"), """{"url":"https://example.com"}"""),
            Is.EqualTo("Hello world"));

    [Test]
    public async Task CutsALongPageAndSaysHowToReadOn()
    {
        var long_ = "<p>" + new string('a', Limits.WebPageChars + 500) + "</p>";

        var first = await Run(Page(long_), """{"url":"https://example.com"}""");
        var second = await Run(Page(long_), $$"""{"url":"https://example.com","start":"{{Limits.WebPageChars}}"}""");

        Assert.That(first, Does.EndWith($"call again with start={Limits.WebPageChars} to read on]"));
        Assert.That(second, Is.EqualTo(new string('a', 500)));
    }

    [TestCase("not a url")]
    [TestCase("file:///etc/passwd")]
    [TestCase("ftp://example.com/x")]
    public void RefusesAddressesThatAreNotWebPages(string url) =>
        Assert.ThrowsAsync<ToolException>(() => Run(Page("x"), $$"""{"url":"{{url}}"}"""));

    [Test]
    public void ReportsAServerError() =>
        Assert.ThrowsAsync<ToolException>(() =>
            Run(new StubHttpHandler(HttpStatusCode.NotFound, "no"), """{"url":"https://example.com/missing"}"""));

    [Test]
    public void ReportsAServerErrorAndPointsToAnotherLink() =>
        Assert.That(Assert.ThrowsAsync<ToolException>(() =>
                Run(new StubHttpHandler(HttpStatusCode.Forbidden, "no"), """{"url":"https://example.com"}"""))!.Message,
            Does.Contain("403").And.Contain("Try another link"));

    [Test]
    public async Task SaysAPageDrawnByScriptsHasNoContentAndPointsToAnotherLink()
    {
        var html = "<html><body><h1>Forecast</h1><div id=\"app\"></div><script>" + new string('x', 30_000) + "</script></body></html>";

        var text = await Run(Page(html), """{"url":"https://example.com"}""");

        Assert.That(text, Does.StartWith("Forecast").And.Contain("fills in its content with scripts").And.Contain("Try another link"));
    }

    [Test]
    public void ReportsAServerThatCannotBeReached() =>
        Assert.ThrowsAsync<ToolException>(() => Run(new UnreachableHttpHandler(), """{"url":"https://example.com"}"""));

    [Test]
    public void NeedsPermissionBecauseItReachesTheNetwork() =>
        Assert.That(new WebFetchTool(new HttpClient()).EffectOf(Json.Parse("{}")).Access, Is.EqualTo(ToolAccess.Network));
}
