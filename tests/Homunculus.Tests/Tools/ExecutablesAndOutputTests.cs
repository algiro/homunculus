using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ExecutablesAndOutputTests
{
    private string? _savedPath;
    private string? _folder;

    [TearDown]
    public void TearDown()
    {
        if (_savedPath is not null) Environment.SetEnvironmentVariable("PATH", _savedPath);
        if (_folder is not null && Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        _savedPath = null;
        _folder = null;
    }

    private void OnlyThisFolderIsOnThePath(string fileName)
    {
        _savedPath = Environment.GetEnvironmentVariable("PATH");
        _folder = Path.Combine(Path.GetTempPath(), "homunculus-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, fileName), "");
        Environment.SetEnvironmentVariable("PATH", _folder);
    }

    [Test]
    public void FindsAProgramOnThePathByItsNameWithoutAnExtension()
    {
        OnlyThisFolderIsOnThePath(OperatingSystem.IsWindows() ? "mytool.cmd" : "mytool");

        Assert.That(Executables.IsOnPath("mytool"), Is.True);
        Assert.That(Executables.IsOnPath("othertool"), Is.False);
    }

    [Test]
    public void FindsAProgramWhenTheExtensionIsGiven()
    {
        OnlyThisFolderIsOnThePath("mytool.exe");

        Assert.That(Executables.IsOnPath("mytool.exe"), Is.True);
    }

    [Test]
    public void FindsNothingWhenThePathIsEmpty()
    {
        _savedPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", "");

        Assert.That(Executables.IsOnPath("dotnet"), Is.False);
    }

    [Test]
    public void LeavesAShortTextAlone() =>
        Assert.That("short".TruncateKeepingEnd(100), Is.EqualTo("short"));

    [Test]
    public void KeepsTheStartAndTheEndOfALongTextAndSaysHowMuchWasLeftOut()
    {
        var text = new string('a', 400) + new string('m', 1000) + new string('z', 600);

        var result = text.TruncateKeepingEnd(1000);

        Assert.That(result, Does.StartWith(new string('a', 300)).And.EndWith(new string('z', 600)));
        Assert.That(result, Does.Contain("[... 1000 characters left out ...]"));
        Assert.That(result.Length, Is.LessThan(1000 + 60));
    }

    [Test]
    public void KeepsMoreOfTheEndThanOfTheStart()
    {
        var result = new string('x', 2000).TruncateKeepingEnd(1000);

        var parts = result.Split("\n[... ", 2);
        Assert.That(parts[0], Has.Length.EqualTo(300));
        Assert.That(parts[1].Split("...]\n")[1], Has.Length.EqualTo(700));
    }

    [Test]
    public async Task ACommandsLongOutputKeepsItsSummaryAndExitCode()
    {
        var runner = new FakeCommandRunner
        {
            Outcome = new CommandOutcome(string.Join('\n', Enumerable.Range(1, 6000).Select(i => $"restore line {i}")) + "\nBuild FAILED: 3 errors\n", 1),
        };
        var tool = new RunCommandTool(runner);

        var result = await tool.RunAsync(Json.Parse("""{"command":"dotnet build"}"""), CancellationToken.None);

        Assert.That(result, Does.StartWith("restore line 1\n"));
        Assert.That(result, Does.Contain("characters left out").And.Contain("Build FAILED: 3 errors").And.EndWith("[exit code 1]"));
        Assert.That(result.Length, Is.LessThan(Limits.CommandOutputChars + 100));
    }

    [Test]
    public async Task AShortOutputIsReturnedWhole()
    {
        var tool = new RunCommandTool(new FakeCommandRunner { Outcome = new CommandOutcome("hello\n", 0) });

        var result = await tool.RunAsync(Json.Parse("""{"command":"echo hello"}"""), CancellationToken.None);

        Assert.That(result, Is.EqualTo("hello\n\n[exit code 0]"));
    }

    [Test]
    public void ReadingAFileStillKeepsTheWholeFirstPart() =>
        Assert.That(Limits.CommandOutputChars, Is.LessThan(Limits.OutputChars), "commands are capped tighter than files");
}
