using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

public class ToolRunnerTests
{
    private TestApp _app = null!;

    private void Start() => _app = new TestApp(new ScriptedModel());

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
        Start();
        _app.Ui.Answers('n');

        var result = await Run("read_file", """{"path":"../outside.txt"}""");

        Assert.That(result, Does.StartWith("Refused by the user."));
    }

    [Test]
    public async Task TellsTheModelNotToRetryOrWorkAroundARefusal()
    {
        Start();
        _app.Ui.Answers('n');

        var result = await Run("read_file", """{"path":"../outside.txt"}""");

        Assert.That(result, Does.Contain("Do not retry it or work around it").And.Contain("tell the user"));
    }

    [Test]
    public async Task DoesNotShowARefusalAsAnError()
    {
        Start();
        _app.Ui.Answers('n');

        await Run("read_file", """{"path":"../outside.txt"}""");

        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("<-"));
    }

    [Test]
    public async Task WarnsTheModelThatKeepsAskingForWhatTheUserRefused()
    {
        Start();
        _app.Ui.Answers('n', 'n');
        await Run("read_file", """{"path":"../outside.txt"}""");

        var second = await Run("read_file", """{"path":"../outside.txt"}""");

        Assert.That(second, Does.Contain("You already made this exact call"));
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
        public ToolEffect EffectOf(System.Text.Json.JsonElement arguments) => new(ToolAccess.Read);
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
        Start();
        _app.Ui.Answers('n');

        await Run("read_file", """{"path":"../outside.txt"}""");

        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("<-"));
    }
}
