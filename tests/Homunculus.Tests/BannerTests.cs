using System.Text.RegularExpressions;

namespace Homunculus.Tests;

public partial class BannerTests
{
    private static readonly BannerDetails Details = new("qwen3:8b", "localhost:11434", @"C:\projects\dotNet\homunculus", "judge");

    [GeneratedRegex(@"\e\[[0-9;]*m")]
    private static partial Regex Escape();

    private static string[] Lines(string banner) => Escape().Replace(banner, "").Split(Environment.NewLine);

    [Test]
    public void TextIsPlainLinesForAPipeOrAFile()
    {
        var banner = Banner.Render(Details, BannerStyle.Text, 80);

        Assert.That(banner.Split(Environment.NewLine), Is.EqualTo(new[]
        {
            "homunculus - a tiny coding agent in plain .NET",
            "model: qwen3:8b (localhost:11434)",
            @"project: C:\projects\dotNet\homunculus",
            "mode: judge",
            "Type /help for commands, /exit to quit.",
        }));
    }

    [Test]
    public void NamesTheJudgeOnlyWhenItIsAnotherModel()
    {
        Assert.That(Banner.Render(Details, BannerStyle.Text, 80), Does.Not.Contain("judge:"));
        Assert.That(Banner.Render(Details with { JudgeModel = "small" }, BannerStyle.Text, 80), Does.Contain("judge: small"));
        Assert.That(Banner.Render(Details with { JudgeModel = "small" }, BannerStyle.Art, 80), Does.Contain("judge    small"));
    }

    [Test]
    public void ArtDrawsTheTitleInABoxWithoutColour()
    {
        var banner = Banner.Render(Details, BannerStyle.Art, 80);

        Assert.That(banner, Does.Not.Contain("\e["));
        Assert.That(banner, Does.Contain(Banner.Title[2]).And.Contain("╭").And.Contain("╯"));
        Assert.That(banner, Does.Contain("model    qwen3:8b · localhost:11434").And.Contain("mode     judge"));
        Assert.That(banner, Does.Not.Contain("▀"), "the flask needs colour, so it is left out");
    }

    [Test]
    public void ColourAddsTheFlaskNextToTheTitle()
    {
        var banner = Banner.Render(Details, BannerStyle.Color, 80);

        Assert.That(banner, Does.Contain("\e[38;2;"));
        Assert.That(Lines(banner), Has.Some.Contains("▀").And.Some.Contains(Banner.Title[0]));
    }

    [Test]
    public void TheTitleSpellsTheName() =>
        Assert.That(Banner.Title, Has.Count.EqualTo(5).And.All.Length.EqualTo(Banner.Title[0].Length));

    [Test]
    public void FitsAnyConsoleWidth([Values(BannerStyle.Art, BannerStyle.Color)] BannerStyle style, [Range(30, 120, 3)] int width) =>
        Assert.That(Lines(Banner.Render(Details, style, width)).Max(line => line.Length), Is.LessThanOrEqualTo(width));

    [Test]
    public void DropsTheFlaskAndThenTheBlockLettersWhenTheConsoleIsNarrow()
    {
        Assert.That(Lines(Banner.Render(Details, BannerStyle.Color, 60)), Has.None.Contains("▀").And.Some.Contains(Banner.Title[0]));
        Assert.That(Lines(Banner.Render(Details, BannerStyle.Color, 40)), Has.None.Contains(Banner.Title[0]).And.Some.Contains("homunculus"));
    }

    [Test]
    public void ShortensALongProjectPathFromTheStartSoItsNameStays()
    {
        var deep = Details with { Project = @"C:\Users\someone\source\repos\clients\big-company\monorepo\services\homunculus" };

        var line = Lines(Banner.Render(deep, BannerStyle.Art, 80)).Single(l => l.Contains("project"));

        Assert.That(line, Does.Contain("…").And.Contain(@"services\homunculus"));
    }

    [Test]
    public void ShowsAllowAllInGold()
    {
        var banner = Banner.Render(Details with { Mode = "allow-all" }, BannerStyle.Color, 80);

        Assert.That(banner, Does.Contain("\e[38;2;255;212;94mallow-all"));
    }

    [Test]
    public void APipeOrAFileGetsPlainText() =>
        Assert.That(ConsoleUserInterface.BannerStyleFor(redirected: true, _ => "anything"), Is.EqualTo(BannerStyle.Text));

    [Test]
    public void NoColorTurnsColourOff() =>
        Assert.That(ConsoleUserInterface.BannerStyleFor(false, name => name is "NO_COLOR" or "WT_SESSION" or "TERM" ? "1" : null), Is.EqualTo(BannerStyle.Art));

    [Test]
    public void UsesColourOnlyWhereTheTerminalSaysItUnderstandsIt()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.That(ConsoleUserInterface.BannerStyleFor(false, name => name == "WT_SESSION" ? "abc" : null), Is.EqualTo(BannerStyle.Color));
            Assert.That(ConsoleUserInterface.BannerStyleFor(false, name => name == "TERM_PROGRAM" ? "vscode" : null), Is.EqualTo(BannerStyle.Color));
            Assert.That(ConsoleUserInterface.BannerStyleFor(false, _ => null), Is.EqualTo(BannerStyle.Art), "an old console window");
        }
        else
        {
            Assert.That(ConsoleUserInterface.BannerStyleFor(false, name => name == "TERM" ? "xterm-256color" : null), Is.EqualTo(BannerStyle.Color));
            Assert.That(ConsoleUserInterface.BannerStyleFor(false, name => name == "TERM" ? "dumb" : null), Is.EqualTo(BannerStyle.Art));
        }
    }
}
