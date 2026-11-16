using System.Text.RegularExpressions;
using Homunculus.Llm;

namespace Homunculus.Tests;

public partial class MarkdownTests
{
    [GeneratedRegex(@"\e\[[0-9;?]*[A-Za-z]")]
    private static partial Regex Escape();

    private static string[] Art(string markdown, int width = 80) =>
        Markdown.Render(markdown, BannerStyle.Art, width).Split(Environment.NewLine);

    private static string[] Shown(string markdown, int width = 80) =>
        Escape().Replace(Markdown.Render(markdown, BannerStyle.Color, width), "").Split(Environment.NewLine);

    [Test]
    public void LeavesMarkdownAsItIsForAPipeOrAFile()
    {
        const string markdown = "# Title\n**bold** and `code`\n| a | b |\n|---|---|\n| 1 | 2 |";

        Assert.That(Markdown.Render(markdown, BannerStyle.Text, 80), Is.EqualTo(markdown));
    }

    [Test]
    public void DrawsATableWithAlignedColumns()
    {
        var lines = Art("| City | Temp |\n|------|-----:|\n| Vega de San Mateo | 18 °C |\n| Las Palmas | 23 °C |");

        Assert.That(lines, Is.EqualTo(new[]
        {
            "┌───────────────────┬───────┐",
            "│ City              │ Temp  │",
            "├───────────────────┼───────┤",
            "│ Vega de San Mateo │ 18 °C │",
            "│ Las Palmas        │ 23 °C │",
            "└───────────────────┴───────┘",
        }));
    }

    [Test]
    public void MeasuresCellsWithoutTheirMarkers()
    {
        var lines = Shown("| Tool | Use |\n|---|---|\n| `read_file` | **reads** a file |\n| `edit_file` | edits |");

        Assert.That(lines.Select(line => line.Length).Distinct(), Has.Exactly(1).Items, "every line of the table is as wide");
        Assert.That(lines[3], Is.EqualTo("│ read_file │ reads a file │"));
    }

    [Test]
    public void TurnsATableTooWideForTheConsoleIntoOneBlockPerRow()
    {
        var lines = Art("| Day | Forecast |\n|---|---|\n| Monday | Sunny with a light breeze from the north-east all day |", width: 40);

        Assert.That(lines, Is.EqualTo(new[]
        {
            "• Day       Monday",
            "  Forecast  Sunny with a light breeze from the north-east all day",
        }));
    }

    [Test]
    public void KeepsAPipeInsideCodeInACell() =>
        Assert.That(Art("| Shell | Means |\n|---|---|\n| `a | b` | pipe |")[3], Is.EqualTo("│ a | b │ pipe  │"));

    [Test]
    public void IndentsCodeBlocksAndKeepsThemAsTheyAre()
    {
        var lines = Art("Run:\n```bash\ndotnet test **not bold**\n```\nDone.");

        Assert.That(lines, Is.EqualTo(new[] { "Run:", "    dotnet test **not bold**", "Done." }));
    }

    [Test]
    public void GivesCodeBlocksABackgroundAndTheirLanguageInColour()
    {
        var rendered = Markdown.Render("```csharp\nvar x = 1;\n```", BannerStyle.Color, 80);

        Assert.That(rendered, Does.Contain("\e[48;2;45;36;56m"));
        Assert.That(Escape().Replace(rendered, "").Split(Environment.NewLine), Is.EqualTo(new[] { "  csharp", "   var x = 1; " }));
    }

    [Test]
    public void ShowsHeadingsWithoutTheirHashes()
    {
        Assert.That(Art("## Weather today"), Is.EqualTo(new[] { "WEATHER TODAY" }));
        Assert.That(Markdown.Render("## Weather today", BannerStyle.Color, 80), Does.StartWith("\e[1m").And.Contain("Weather today"));
    }

    [Test]
    public void DrawsListsQuotesAndRules()
    {
        Assert.That(Art("- one\n  * nested\n1. first\n- [x] done\n- [ ] todo"), Is.EqualTo(new[] { "• one", "  • nested", "1. first", "• ☑ done", "• ☐ todo" }));
        Assert.That(Art("> quoted"), Is.EqualTo(new[] { "│ quoted" }));
        Assert.That(Art("---", width: 30), Is.EqualTo(new[] { new string('─', 29) }));
    }

    [TestCase("**bold** text", "bold text")]
    [TestCase("an *italic* word", "an italic word")]
    [TestCase("run `dotnet test` now", "run dotnet test now")]
    [TestCase("see [the docs](https://example.com)", "see the docs (https://example.com)")]
    [TestCase("<https://example.com>", "<https://example.com>")]
    public void RemovesInlineMarkersWithoutColour(string markdown, string shown) =>
        Assert.That(Art(markdown).Single(), Is.EqualTo(shown));

    [TestCase("call read_file and edit_file")]
    [TestCase("2*3*4 = 24")]
    [TestCase("snake_case_name")]
    [TestCase("a * b * c")]
    [TestCase("__init__.py")]
    public void LeavesUnderscoresAndAsterisksInsideWordsAlone(string text) =>
        Assert.That(Art(text).Single(), Is.EqualTo(text));

    [Test]
    public void StylesInlineMarkersInColour()
    {
        var line = Markdown.Render("**bold**, *italic*, `code`", BannerStyle.Color, 80);

        Assert.That(line, Does.Contain("\e[1mbold").And.Contain("\e[3mitalic").And.Contain("\e[38;2;155;232;193mcode"));
    }

    [Test]
    public void SumsUpATurnOnOneLine()
    {
        var turn = new TurnSummary("qwen3:8b", new TokenUsage(1620, 244, 1864, Cached: 813), 2, TimeSpan.FromSeconds(45.3));

        Assert.That(ConsoleUserInterface.TurnSummaryLine(turn, BannerStyle.Art), Is.EqualTo("[qwen3:8b · 1.6k in (813 cached) · 244 out · 2 tools · 45.3s]"));
        Assert.That(ConsoleUserInterface.TurnSummaryLine(turn, BannerStyle.Color), Does.StartWith("\e[38;2;138;131;153mqwen3:8b"));
        Assert.That(ConsoleUserInterface.TurnSummaryLine(turn with { Usage = null, ToolCalls = 0, Model = null }, BannerStyle.Art), Is.EqualTo("[45.3s]"));
    }

    [TestCase(340, "340")]
    [TestCase(1620, "1.6k")]
    [TestCase(12_400, "12k")]
    [TestCase(1_250_000, "1.3M")]
    public void WritesTokenCountsCompactly(int count, string text) =>
        Assert.That(UsageText.Compact(count), Is.EqualTo(text));
}
