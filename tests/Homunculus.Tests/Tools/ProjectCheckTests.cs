using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ProjectCheckTests
{
    private FakeCommandRunner _runner = null!;
    private ProjectCheck _check = null!;

    [SetUp]
    public void SetUp()
    {
        _runner = new FakeCommandRunner();
        _check = new ProjectCheck("dotnet build", _runner);
    }

    private void Prints(string output, int exitCode) => _runner.Outcome = new CommandOutcome(output, exitCode);

    [Test]
    public async Task RunsTheConfiguredCommand()
    {
        await _check.RunAsync(CancellationToken.None);

        Assert.That(_runner.Commands, Is.EqualTo(new[] { "dotnet build" }));
    }

    [Test]
    public async Task PassesWhenTheCommandExitsWithZero()
    {
        Prints("lots of output\n", 0);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(new CheckResult(true, "Check passed.")));
    }

    [Test]
    public async Task FailsWithTheExitCodeAndOnlyTheErrorLines()
    {
        Prints("Restore complete\nApp.cs(5,1): error CS1002: ; expected\nBuild FAILED.\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Passed, Is.False);
        Assert.That(result.Report, Is.EqualTo("Check `dotnet build` failed (exit code 1):\nApp.cs(5,1): error CS1002: ; expected"));
    }

    [Test]
    public async Task ShowsAnErrorOnceEvenThoughABuildPrintsItTwiceAndDropsTheProjectSuffix()
    {
        Prints(
            "C:\\p\\App.cs(5,1): error CS1002: ; expected [C:\\p\\Homunculus.csproj]\r\n" +
            "C:\\p\\App.cs(5,1): error CS1002: ; expected [C:\\p\\Homunculus.csproj]\r\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Report, Does.EndWith("(exit code 1):\nC:\\p\\App.cs(5,1): error CS1002: ; expected"));
    }

    [Test]
    public async Task StartsPathsAtTheProjectWhenItKnowsWhereTheProjectIs()
    {
        var project = new ProjectDirectory(Path.Combine(Path.GetTempPath(), "check-project"));
        var check = new ProjectCheck("dotnet build", _runner, project);
        Prints($"{project.Root}{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}App.cs(5,1): error CS1002: ; expected\n", 1);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.That(result.Report, Does.EndWith($"(exit code 1):\nsrc{Path.DirectorySeparatorChar}App.cs(5,1): error CS1002: ; expected"));
    }

    [Test]
    public async Task ShowsTheLastLinesWhenNothingMentionsAnError()
    {
        Prints(string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}")), 2);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Report, Does.Contain("line 40").And.Contain("line 26").And.Not.Contain("line 25\n"));
    }

    [Test]
    public async Task CutsALongListOfErrorsAndSaysHowManyWereLeftOut()
    {
        Prints(string.Join('\n', Enumerable.Range(1, 20).Select(i => $"error E{i:D2}")), 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Report, Does.Contain("error E15").And.Not.Contain("error E16").And.EndWith("[5 more lines]"));
    }

    [Test]
    public async Task CutsAVeryLongReport()
    {
        Prints("error " + new string('x', 5000), 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Report, Has.Length.LessThan(3200).And.EndWith("..."));
    }

    [Test]
    public async Task ReportsACommandThatCouldNotFinish()
    {
        _runner.Failure = new ToolException("Command timed out after 60 seconds.");

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(new CheckResult(false, "Check `dotnet build` did not finish: Command timed out after 60 seconds.")));
    }

    [Test]
    public void LetsACancellationThrough()
    {
        _runner.Failure = new OperationCanceledException();

        Assert.CatchAsync<OperationCanceledException>(() => _check.RunAsync(CancellationToken.None));
    }
}
