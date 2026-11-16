using NSubstitute;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

public class ToolDisplayTests
{
    private TempProject _project = null!;

    [SetUp] public void SetUp() => _project = new TempProject();
    [TearDown] public void TearDown() => _project.Dispose();

    private string Call(ITool tool, string json) => ToolDisplay.DescribeCall(tool, Json.Parse(json), _project.Root);

    private ToolOutcome Result(string result, bool refused = false) =>
        ToolDisplay.DescribeResult(result, _project.Root, TimeSpan.Zero, refused);

    [Test]
    public void NamesAFileByItsPathInTheProject() =>
        Assert.That(Call(new ReadFileTool(_project.Location), """{"path":"src/App.cs"}"""), Is.EqualTo(Path.Combine("src", "App.cs")));

    [Test]
    public void NamesTheProjectItselfAsADot() =>
        Assert.That(Call(new ListFilesTool(_project.Location), """{"path":"."}"""), Is.EqualTo("."));

    [Test]
    public void ShowsACommandAsACommandLine() =>
        Assert.That(
            Call(new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1)), """{"program":"dotnet","args":["test","--filter","Name with space"]}"""),
            Is.EqualTo("dotnet test --filter \"Name with space\""));

    [Test]
    public void CountsTheLinesAnEditAddsAndRemoves() =>
        Assert.That(Call(new EditFileTool(_project.Location), """{"path":"a.txt","old_str":"x\ny","new_str":"x\ny\nz"}"""), Is.EqualTo("a.txt  +3 −2"));

    [Test]
    public void CountsThePlanSteps() =>
        Assert.That(Call(new PlanTool(), """{"steps":["read","fix"]}"""), Is.EqualTo("2 steps"));

    [Test]
    public void ShowsWhatAWebCallLooksFor()
    {
        using var http = new HttpClient();

        Assert.That(Call(new WebFetchTool(http), """{"url":"https://example.com/weather"}"""), Is.EqualTo("https://example.com/weather"));
        Assert.That(Call(new WebSearchTool(Substitute.For<ISearchProvider>()), """{"query":"weather in Las Palmas"}"""), Is.EqualTo("\"weather in Las Palmas\""));
    }

    [Test]
    public void FallsBackToTheArgumentsWhenTheyMakeNoSense() =>
        Assert.That(Call(new ReadFileTool(_project.Location), """{"file":"a.txt"}"""), Is.EqualTo("""{"file":"a.txt"}"""));

    [Test]
    public void ShortensALongCall() =>
        Assert.That(
            Call(new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1)), "{\"program\":\"git\",\"args\":[\"" + new string('a', 200) + "\"]}"),
            Has.Length.EqualTo(70).And.EndsWith("…"));

    [Test]
    public void AnErrorFailsWithItsFirstLine() =>
        Assert.That(Result("Error: File not found: a.txt\nmore"), Is.EqualTo(new ToolOutcome(true, "Error: File not found: a.txt", TimeSpan.Zero)));

    [Test]
    public void ARefusalFails() =>
        Assert.That(Result("Refused by the user.", refused: true).Failed, Is.True);

    [Test]
    public void ACommandShowsItsExitCodeAndOutputSize()
    {
        Assert.That(Result("ok\nall passed\n[exit code 0]"), Is.EqualTo(new ToolOutcome(false, "exit 0 · 2 lines", TimeSpan.Zero)));
        Assert.That(Result("boom\n[exit code 1]"), Is.EqualTo(new ToolOutcome(true, "exit 1 · 1 line", TimeSpan.Zero)));
        Assert.That(Result("\n[exit code 0]").Summary, Is.EqualTo("exit 0 · no output"));
    }

    [Test]
    public void CountsTheLinesOfALongerResult() =>
        Assert.That(Result("a\nb\nc\n").Summary, Is.EqualTo("3 lines"));

    [Test]
    public void ShowsAOneLineResultWithPathsInTheProjectShortened() =>
        Assert.That(Result($"Edited {Path.Combine(_project.Root, "src", "a.txt")}").Summary, Is.EqualTo($"Edited {Path.Combine("src", "a.txt")}"));

    [Test]
    public void SaysWhenThereWasNoOutput() =>
        Assert.That(Result("  ").Summary, Is.EqualTo("no output"));

    [Test]
    public void ShowsTheTimeOnlyWhenItWasLongEnoughToNotice()
    {
        Assert.That(ToolDisplay.Duration(TimeSpan.FromSeconds(0.2)), Is.Null);
        Assert.That(ToolDisplay.Duration(TimeSpan.FromSeconds(1.5)), Is.EqualTo("1.5s"));
    }

    [Test]
    public void DrawsTheCallAndResultForEachKindOfOutput()
    {
        var ok = new ToolOutcome(false, "3 lines", TimeSpan.FromSeconds(2));
        var failed = new ToolOutcome(true, "exit 1 · 1 line", TimeSpan.Zero);

        Assert.That(ConsoleUserInterface.ToolCallLine("read_file", "a.txt", BannerStyle.Text), Is.EqualTo("-> read_file  a.txt"));
        Assert.That(ConsoleUserInterface.ToolCallLine("read_file", "a.txt", BannerStyle.Art), Is.EqualTo("● read_file  a.txt"));
        Assert.That(ConsoleUserInterface.ToolCallLine("read_file", "a.txt", BannerStyle.Color), Does.Contain("\e[1mread_file\e[0m  a.txt"));

        Assert.That(ConsoleUserInterface.ToolResultLine(ok, BannerStyle.Text), Is.EqualTo("   = 3 lines · 2.0s"));
        Assert.That(ConsoleUserInterface.ToolResultLine(failed, BannerStyle.Text), Is.EqualTo("   ! exit 1 · 1 line"));
        Assert.That(ConsoleUserInterface.ToolResultLine(ok, BannerStyle.Art), Is.EqualTo("  └  3 lines · 2.0s"));
        Assert.That(ConsoleUserInterface.ToolResultLine(failed, BannerStyle.Color), Does.Contain("\e[38;2;239;108;108mexit 1"));
    }

    [Test]
    public void TheProgressLineDrawsOverItselfAndCountsSeconds()
    {
        var early = ConsoleUserInterface.ProgressLine("thinking", 0, TimeSpan.FromSeconds(0.4), BannerStyle.Art);
        var later = ConsoleUserInterface.ProgressLine("thinking", 1, TimeSpan.FromSeconds(3.7), BannerStyle.Color);

        Assert.That(early, Does.StartWith("\r").And.Contain("thinking...").And.Not.Contain("s "));
        Assert.That(later, Does.StartWith("\r").And.Contain("thinking… 3s"));
    }
}
