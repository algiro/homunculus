using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

public class ToolRunnerTests
{
    private TestApp _app = null!;
    private ToolRunner _runner = null!;

    private void Start(string mode = "allow-all")
    {
        _app = new TestApp(new ScriptedModel(), mode);
        _runner = new ToolRunner(_app.Tools, _app.Approvals, _app.Ui);
    }

    [TearDown] public void TearDown() => _app.Dispose();

    private Task<string> Run(string tool, string json) =>
        _runner.RunAsync(new ToolCall("c1", tool, Json.Parse(json)), CancellationToken.None);

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
}
