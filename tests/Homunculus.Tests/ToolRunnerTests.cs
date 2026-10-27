using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

public class ToolRunnerTests
{
    private TestApp _app = null!;

    private void Start(string mode = "allow-all") => _app = new TestApp(new ScriptedModel(), mode);

    [TearDown] public void TearDown() => _app.Dispose();

    private Task<string> Run(string tool, string json) =>
        _app.Runner.RunAsync(new ToolCall("c1", tool, Json.Parse(json)), CancellationToken.None);

    [Test]
    public async Task ReturnsTheToolOutput()
    {
        Start();
        _app.Project.Write("a.txt", "hi");

        Assert.That(await Run("read_file", """{"path":"a.txt"}"""), Is.EqualTo("hi"));
    }

    [Test]
    public async Task AnnouncesTheCall()
    {
        Start();

        await Run("list_files", "{}");

        Assert.That(_app.Ui.Output.Single(), Does.Contain("list_files"));
    }

    [Test]
    public async Task ShowsThePlanTheModelRecorded()
    {
        Start();

        await Run("plan", """{"steps":["[x] read","[ ] edit"]}""");

        Assert.That(_app.Ui.Output, Has.Count.EqualTo(2));
        Assert.That(_app.Ui.Output[1], Is.EqualTo("1. [x] read\n2. [ ] edit"));
    }

    [Test]
    public async Task DoesNotShowAnythingForAnEmptyPlan()
    {
        Start();

        await Run("plan", """{"steps":[]}""");

        Assert.That(_app.Ui.Output, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ShowsTheErrorInsteadOfAPlanWhenTheCallFails()
    {
        Start();

        await Run("plan", "{}");

        Assert.That(_app.Ui.Output, Has.Count.EqualTo(2));
        Assert.That(_app.Ui.Output[1], Does.StartWith("  <- Error:"));
    }

    [Test]
    public async Task DoesNotShowTheOutputOfOtherTools()
    {
        Start();
        _app.Project.Write("a.txt", "secret content");

        await Run("read_file", """{"path":"a.txt"}""");

        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("secret content"));
    }

    [Test]
    public async Task ReportsAnUnknownTool()
    {
        Start();

        Assert.That(await Run("fly", "{}"), Is.EqualTo("Error: unknown tool 'fly'."));
    }

    [Test]
    public async Task TurnsToolFailuresIntoText()
    {
        Start();

        Assert.That(await Run("read_file", """{"path":"nope.txt"}"""), Does.StartWith("Error: File not found"));
    }

    [Test]
    public async Task TurnsOperatingSystemFailuresIntoText()
    {
        Start();
        var path = _app.Project.Write("busy.txt", "a");
        using var lockedByAnotherProgram = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.That(await Run("edit_file", """{"path":"busy.txt","old_str":"a","new_str":"b"}"""), Does.StartWith("Error:"));
    }

    [Test]
    public async Task ReturnsTheRefusalWithoutRunningTheTool()
    {
        Start("effect");
        _app.Ui.Answers('n');

        var result = await Run("run_command", """{"command":"echo hi"}""");

        Assert.That(result, Is.EqualTo("Refused by the user."));
    }

    [Test]
    public void DoesNotHideUnexpectedToolFailures()
    {
        Start();
        var runner = new ToolRunner(
            new ToolRegistry([new BrokenTool()]),
            new Approvals(new FixedPolicy(Verdict.Allow), _app.Ui),
            _app.Ui);

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(new ToolCall("c1", "broken", Json.Parse("{}")), CancellationToken.None));
    }

    private sealed class BrokenTool : ITool
    {
        public string Name => "broken";
        public string Description => "Always fails with a bug.";
        public System.Text.Json.Nodes.JsonObject Parameters => Schema.Of();
        public ToolEffect EffectOf(System.Text.Json.JsonElement arguments) => ToolEffect.Planning;
        public Task<string> RunAsync(System.Text.Json.JsonElement arguments, CancellationToken cancellation) =>
            throw new InvalidOperationException("bug");
    }

    [Test]
    public async Task ShowsTheUserWhyAToolFailed()
    {
        Start();

        await Run("read_file", """{"path":"nope.txt"}""");

        Assert.That(_app.Ui.Output[^1], Does.StartWith("  <- Error: File not found"));
    }

    [Test]
    public async Task ShowsTheUserThatAToolIsUnknown()
    {
        Start();

        await Run("fly", "{}");

        Assert.That(_app.Ui.Output.Single(), Is.EqualTo("  <- Error: unknown tool 'fly'."));
    }

    [Test]
    public async Task ShowsNoFailureLineWhenTheToolWorks()
    {
        Start();

        await Run("list_files", "{}");

        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("<-"));
    }

    [Test]
    public async Task DoesNotCallARefusalByTheUserAFailure()
    {
        Start("effect");
        _app.Ui.Answers('n');

        await Run("run_command", """{"command":"echo hi"}""");

        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("<-"));
    }
}
