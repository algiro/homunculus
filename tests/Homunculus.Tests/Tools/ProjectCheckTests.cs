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

        Assert.That(result.Passed, Is.False);
        Assert.That(result.DidNotFinish, Is.True);
        Assert.That(result.Report, Is.EqualTo("Check `dotnet build` did not finish: Command timed out after 60 seconds."));
    }

    [Test]
    public void LetsACancellationThrough()
    {
        _runner.Failure = new OperationCanceledException();

        Assert.CatchAsync<OperationCanceledException>(() => _check.RunAsync(CancellationToken.None));
    }

    [Test]
    public async Task KeepsEachErrorWithoutItsPositionSoMovedErrorsMatch()
    {
        Prints("src\\App.cs(5,1): error CS1002: ; expected\nmain.c:12:5: error: expected ';'\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors.Select(error => error.Key), Is.EqualTo(new[]
        {
            "src\\App.cs: error CS1002: ; expected",
            "main.c: error: expected ';'",
        }));
        Assert.That(result.Errors[0].Line, Is.EqualTo("src\\App.cs(5,1): error CS1002: ; expected"));
    }

    [Test]
    public async Task GivesTheSameKeyToTheSameErrorAtAnotherLine()
    {
        Prints("App.cs(5,1): error CS1002: ; expected\n", 1);
        var first = await _check.RunAsync(CancellationToken.None);
        Prints("App.cs(9,3): error CS1002: ; expected\n", 1);
        var second = await _check.RunAsync(CancellationToken.None);

        Assert.That(second.Errors[0].Key, Is.EqualTo(first.Errors[0].Key));
    }

    [Test]
    public async Task HasNoErrorsWhenTheOutputNamesNone()
    {
        Prints("3 tests failed\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.DidNotFinish, Is.False);
    }

    [Test]
    public async Task DoesNotTakeABuildsTotalsForAnError()
    {
        Prints("App.cs(5,1): error CS1002: ; expected\n    1 Error(s)\nBuild FAILED.\n    0 error(s)\nNo errors\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors.Select(error => error.Line), Is.EqualTo(new[] { "App.cs(5,1): error CS1002: ; expected" }));
    }

    [Test]
    public async Task HasNoErrorsWhenItPasses()
    {
        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public async Task KeepsTheIndentedLinesUnderAnErrorSoATracebackShowsItsLineNumber()
    {
        Prints("*** Error compiling './main.py'...\n  File \"./main.py\", line 1\n    def broken(:\n               ^\nSyntaxError: invalid syntax\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Report, Does.Contain("*** Error compiling './main.py'...\n  File \"./main.py\", line 1\n    def broken(:\n               ^\nSyntaxError: invalid syntax"));
        Assert.That(result.Errors[0].Context, Has.Count.EqualTo(3));
        Assert.That(result.Errors[0].Line, Is.EqualTo("*** Error compiling './main.py'..."));
    }

    [Test]
    public async Task KeepsAtMostThreeLinesOfContext()
    {
        Prints("main.c:4:5: error: oops\n  one\n  two\n  three\n  four\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors[0].Context, Is.EqualTo(new[] { "  one", "  two", "  three" }));
    }

    [Test]
    public async Task StopsTheContextAtTheNextErrorAndAtABuildsTotals()
    {
        Prints("a.c:1:1: error: first\n  excerpt\nb.c:2:2: error: second\n    1 Error(s)\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors.Select(error => error.Display), Is.EqualTo(new[] { "a.c:1:1: error: first\n  excerpt", "b.c:2:2: error: second" }));
    }

    [Test]
    public async Task ShowsAnErrorThatIsPrintedTwiceWithItsContextOnce()
    {
        Prints("a.c:1:1: error: first\n  excerpt\na.c:1:1: error: first\n  excerpt\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ComparesErrorsByTheirLineAloneNotByTheirContext()
    {
        Prints("a.c:1:1: error: first\n  excerpt one\n", 1);
        var first = await _check.RunAsync(CancellationToken.None);
        Prints("a.c:9:4: error: first\n  excerpt two\n", 1);
        var second = await _check.RunAsync(CancellationToken.None);

        Assert.That(second.Errors[0].Key, Is.EqualTo(first.Errors[0].Key));
    }

    [Test]
    public async Task TakesACompilerLineThatPointsAtAFileAsAnErrorEvenWithoutTheWordError()
    {
        Prints("# example.com/demo\n./main.go:6:14: undefined: undefinedName\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors.Select(error => error.Line), Is.EqualTo(new[] { "./main.go:6:14: undefined: undefinedName" }));
        Assert.That(result.Errors[0].Key, Is.EqualTo("./main.go: undefined: undefinedName"));
        Assert.That(result.Report, Does.EndWith("./main.go:6:14: undefined: undefinedName"));
    }

    [Test]
    public async Task DoesNotTakeAWarningThatPointsAtAFileForAnError()
    {
        Prints("main.c:3:5: warning: unused variable\nmain.c:9:1: error: expected ';'\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors.Select(error => error.Line), Is.EqualTo(new[] { "main.c:9:1: error: expected ';'" }));
    }

    [Test]
    public async Task DoesNotTakeAnOrdinaryLineWithAColonForAnError()
    {
        Prints("Restoring packages: 3 of 5\nresult: done\n", 1);

        var result = await _check.RunAsync(CancellationToken.None);

        Assert.That(result.Errors, Is.Empty);
    }
}
