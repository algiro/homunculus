using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class RunCommandToolTests
{
    private TempProject _project = null!;

    [SetUp] public void SetUp() => _project = new TempProject();
    [TearDown] public void TearDown() => _project.Dispose();

    private RunCommandTool ToolWithTimeout(TimeSpan timeout) => new(_project.Location, timeout);

    private static Task<string> Run(RunCommandTool tool, string command, CancellationToken cancellation = default) =>
        tool.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(new { command })), cancellation);

    private static readonly bool IsCmd = Shell.Detect().Name == "cmd.exe";

    private static string Sleep(int seconds) =>
        OperatingSystem.IsWindows() ? $"ping -n {seconds + 1} 127.0.0.1" : $"sleep {seconds}";

    [Test]
    public async Task ReturnsTheOutputAndExitCode()
    {
        var result = await Run(ToolWithTimeout(TimeSpan.FromSeconds(30)), "echo hello");

        Assert.That(result, Does.Contain("hello"));
        Assert.That(result, Does.Contain("[exit code 0]"));
    }

    [Test]
    public async Task ReportsAFailingExitCode()
    {
        var result = await Run(ToolWithTimeout(TimeSpan.FromSeconds(30)), "exit 3");

        Assert.That(result, Does.Contain("[exit code 3]"));
    }

    [Test]
    public async Task RunsInTheProjectDirectory()
    {
        var command = IsCmd ? "cd" : "pwd";

        var result = await Run(ToolWithTimeout(TimeSpan.FromSeconds(30)), command);

        Assert.That(result, Does.Contain(Path.GetFileName(_project.Root)));
    }

    [Test]
    public async Task CapturesErrorOutputToo()
    {
        var result = await Run(ToolWithTimeout(TimeSpan.FromSeconds(30)), IsCmd || !OperatingSystem.IsWindows() ? "echo oops 1>&2" : "[Console]::Error.WriteLine('oops')");

        Assert.That(result, Does.Contain("oops"));
    }

    [Test]
    public void FailsWhenTheCommandRunsLongerThanTheTimeout()
    {
        var error = Assert.ThrowsAsync<ToolException>(() =>
            Run(ToolWithTimeout(TimeSpan.FromMilliseconds(500)), Sleep(10)));

        Assert.That(error!.Message, Does.Contain("timed out"));
    }

    [Test]
    public void StopsWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        Assert.CatchAsync<OperationCanceledException>(() =>
            Run(ToolWithTimeout(TimeSpan.FromSeconds(30)), Sleep(10), cancellation.Token));
    }

    [Test]
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

    [Test]
    public void ReportsAShellThatCannotBeStartedAsAToolError()
    {
        var tool = new RunCommandTool(_project.Location, TimeSpan.FromSeconds(5), new Shell("ghost-shell", "no-such-shell-program", ["-c"]));

        var error = Assert.ThrowsAsync<ToolException>(() => Run(tool, "echo hi"));

        Assert.That(error!.Message, Does.StartWith("Cannot start ghost-shell"));
    }
}
