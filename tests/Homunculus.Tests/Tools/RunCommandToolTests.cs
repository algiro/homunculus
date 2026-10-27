using System.Text.Json;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class RunCommandToolTests
{
    private TempProject _project = null!;

    [SetUp] public void SetUp() => _project = new TempProject();
    [TearDown] public void TearDown() => _project.Dispose();

    private RunCommandTool ToolWithTimeout(TimeSpan timeout) => new(_project.Location, timeout);

    private RunCommandTool Tool() => ToolWithTimeout(TimeSpan.FromSeconds(30));

    private static readonly bool IsCmd = Shell.Detect().Name == "cmd.exe";

    private static Task<string> Run(RunCommandTool tool, string program, string[] args, CancellationToken cancellation = default) =>
        tool.RunAsync(Json.Parse(JsonSerializer.Serialize(new { program, args })), cancellation);

    /// <summary>A shell started on purpose, as a program like any other, to produce output, errors and exit codes in a test.</summary>
    private static Task<string> RunShell(RunCommandTool tool, string line, CancellationToken cancellation = default) =>
        OperatingSystem.IsWindows()
            ? Run(tool, "cmd", ["/c", line], cancellation)
            : Run(tool, "sh", ["-c", line], cancellation);
    private static string Sleep(int seconds) =>
        OperatingSystem.IsWindows() ? $"ping -n {seconds + 1} 127.0.0.1" : $"sleep {seconds}";

    [Test]
    public async Task ReturnsTheOutputAndExitCode()
    {
        var result = await RunShell(Tool(), "echo hello");

        Assert.That(result, Does.Contain("hello"));
        Assert.That(result, Does.Contain("[exit code 0]"));
    }

    [Test]
    public async Task ReportsAFailingExitCode() =>
        Assert.That(await RunShell(Tool(), "exit 3"), Does.Contain("[exit code 3]"));

    [Test]
    public async Task RunsInTheProjectDirectory()
    {
        var result = await RunShell(Tool(), OperatingSystem.IsWindows() ? "cd" : "pwd");

        Assert.That(result, Does.Contain(Path.GetFileName(_project.Root)));
    }

    [Test]
    public async Task CapturesErrorOutputToo() =>
        Assert.That(await RunShell(Tool(), "echo oops 1>&2"), Does.Contain("oops"));

    [Test]
    public void FailsWhenTheCommandRunsLongerThanTheTimeout()
    {
        var command = IsCmd ? "cd" : "pwd";
        var error = Assert.ThrowsAsync<ToolException>(() => RunShell(ToolWithTimeout(TimeSpan.FromMilliseconds(500)), Sleep(10)));

        Assert.That(error!.Message, Does.Contain("timed out"));
    }

    [Test]
    public void StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        Assert.CatchAsync<OperationCanceledException>(() => RunShell(Tool(), Sleep(10), cancellation.Token));
    }

    [Test]
    public async Task GivesShellCharactersToTheProgramSoNothingElseRuns()
    {
        var result = await Run(ToolWithTimeout(TimeSpan.FromSeconds(30)), IsCmd || !OperatingSystem.IsWindows() ? "echo oops 1>&2" : "[Console]::Error.WriteLine('oops')");
        string[] args = ["--version", ";", "mkdir", "pwned1", "&&", "mkdir", "pwned2", "|", "mkdir", "pwned3", "&", "mkdir", "pwned4", "$(mkdir pwned5)", "`mkdir pwned6`", ">", "pwned7"];

        await Run(Tool(), "dotnet", args);

        Assert.That(Enumerable.Range(1, 7).Where(i => _project.Exists($"pwned{i}")), Is.Empty);
    }

    [Test]
    public void ExplainsTheFormatWhenGivenACommandLine()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Tool().RunAsync(Json.Parse("""{"command":"echo hi"}"""), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("""{"program":"dotnet","args":["test"]}""").And.Contain("No shell"));
    }

    [Test]
    public void ShowsHowToSplitAProgramThatIsAWholeCommandLine()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Tool().RunAsync(Json.Parse("""{"program":"dotnet test --no-build"}"""), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("""{"program":"dotnet","args":["test","--no-build"]}"""));
    }

    [TestCase("./git")]
    [TestCase("../git")]
    [TestCase("bin/git")]
    [TestCase("..")]
    public void RefusesAPathAsTheProgram(string program)
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run(Tool(), program, []));

        Assert.That(error!.Message, Does.Contain("name only"));
    }

    [Test]
    public void RefusesAWindowsPathAsTheProgram()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("A backslash is only a path separator on Windows.");

        Assert.ThrowsAsync<ToolException>(() => Run(Tool(), @"C:\Tools\git.exe", []));
    }

    [Test]
    public void RefusesArgumentsThatAreNotAList() =>
        Assert.ThrowsAsync<ToolException>(() => Tool().RunAsync(Json.Parse("""{"program":"dotnet","args":"test"}"""), CancellationToken.None));

    [Test]
    public void SaysSoWhenTheProgramIsNotOnPath()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run(Tool(), "no-such-program-homunculus", []));

        Assert.That(error!.Message, Does.Contain("not found on PATH"));
    }

    [Test]
    public void RunsAProgramWithoutArguments() =>
        Assert.DoesNotThrowAsync(() => Tool().RunAsync(Json.Parse("""{"program":"dotnet"}"""), CancellationToken.None));

    [Test]
    public void IsACommandNamingItsProgramAndExactArguments()
    {
        var effect = Tool().EffectOf(Json.Parse("""{"program":"dotnet","args":["test"]}"""));

        Assert.That(effect, Is.EqualTo(ToolEffect.Running("dotnet", ["test"])));
        Assert.That(effect.Program, Is.EqualTo("dotnet"));
        Assert.That(effect.Path, Is.Null);
    }

    [Test]
    public void TwoCommandsAreTheSameOnlyWithTheSameArguments()
    {
        var tool = Tool();
        var test = tool.EffectOf(Json.Parse("""{"program":"dotnet","args":["test"]}"""));

        Assert.That(tool.EffectOf(Json.Parse("""{ "args": [ "test" ], "program": "dotnet" }""")).Command, Is.EqualTo(test.Command));
        Assert.That(tool.EffectOf(Json.Parse("""{"program":"dotnet","args":["test","x"]}""")).Command, Is.Not.EqualTo(test.Command));
        Assert.That(tool.EffectOf(Json.Parse("""{"program":"dotnet","args":["test x"]}""")).Command, Is.Not.EqualTo(test.Command));
    }

    [Test]
    public void TellsTheModelThereIsNoShellAndWhichProgramsAreAllowed()
    {
        var tool = new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1), new AllowedPrograms(["dotnet", "npm"]));

        Assert.That(tool.Description, Does.Contain("No shell").And.Contain("Allowed programs: dotnet, npm").And.Contain("read_file"));
        Assert.That(tool.Parameters["required"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "program" }));
    }

    public void IsACommandWithoutAFileTarget() =>
        Assert.That(ToolWithTimeout(TimeSpan.FromSeconds(1)).EffectOf(Json.Parse("{}")), Is.EqualTo(ToolEffect.Running));

    [Test]
    public void NamesTheShellItRunsInItsDescription()
    {
        var detected = Shell.Detect();
        var tool = new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1), detected with { Name = "custom-shell" });

        Assert.That(tool.Description, Does.Contain("custom-shell"));
    }

    [Test]
    public void NamesTheRealShellOfThisMachineByDefault() =>
        Assert.That(ToolWithTimeout(TimeSpan.FromSeconds(1)).Description,
            Does.Contain(Shell.Detect().Name));

    [Test]
    public async Task RunsCommandsInTheCmdFallbackToo()
    {
        Assume.That(OperatingSystem.IsWindows());
        var cmd = Shell.Detect(windows: true, _ => false);

        var result = await Run(new RunCommandTool(_project.Location, TimeSpan.FromSeconds(30), cmd), "echo hello & exit 3");

        Assert.That(result, Does.Contain("hello").And.Contain("[exit code 3]"));
    }
}
