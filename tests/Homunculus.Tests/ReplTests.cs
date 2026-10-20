using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

public class ReplTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private async Task Run(ILanguageModel model, params string[] lines)
    {
        _app = new TestApp(model, "effect");
        _app.Ui.TypesLines(lines);
        await _app.NewRepl().RunAsync();
    }

    [Test]
    public async Task SendsPlainTextToTheAgent()
    {
        var model = new ScriptedModel(Reply.Text("hello there"));

        await Run(model, "hi");

        Assert.That(_app.Ui.Output, Does.Contain("hello there"));
        Assert.That(model.LastConversation[^1].Content, Is.EqualTo("hi"));
    }

    [Test]
    public async Task EndsWhenTheInputEnds()
    {
        await Run(new ScriptedModel());

        Assert.That(_app.Ui.Output[0], Does.Contain("/help"));
    }

    [Test]
    public async Task EndsOnExitWithoutReadingFurther()
    {
        var model = new ScriptedModel(Reply.Text("never"));

        await Run(model, "/exit", "hi");

        Assert.That(model.Calls, Is.Zero);
    }

    [Test]
    public async Task IgnoresBlankLines()
    {
        var model = new ScriptedModel();

        await Run(model, "", "   ");

        Assert.That(model.Calls, Is.Zero);
    }

    [Test]
    public async Task ShowsHelp()
    {
        await Run(new ScriptedModel(), "/help");

        Assert.That(_app.Ui.AllOutput, Does.Contain("/exit").And.Contain("/policy"));
    }

    [Test]
    public async Task ListsTheTools()
    {
        await Run(new ScriptedModel(), "/tools");

        Assert.That(_app.Ui.AllOutput, Does.Contain("read_file").And.Contain("run_command").And.Contain("plan"));
    }

    [Test]
    public async Task ClearsTheConversation()
    {
        var model = new ScriptedModel(Reply.Text("one"), Reply.Text("two"));

        await Run(model, "first", "/clear", "second");

        Assert.That(_app.Ui.AllOutput, Does.Contain("Conversation cleared."));
        Assert.That(model.LastConversation.Select(m => m.Role), Is.EqualTo(new[] { Role.System, Role.User }));
    }

    [Test]
    public async Task ShowsTheCurrentPolicyAndTheAvailableModes()
    {
        await Run(new ScriptedModel(), "/policy");

        Assert.That(_app.Ui.AllOutput, Does.Contain("Policy: effect").And.Contain("allow-all"));
    }

    [Test]
    public async Task ChangesThePolicy()
    {
        await Run(new ScriptedModel(), "/policy allow-all");

        Assert.That(_app.Policy.Mode, Is.EqualTo("allow-all"));
        Assert.That(_app.Ui.AllOutput, Does.Contain("Policy: allow-all"));
    }

    [Test]
    public async Task RejectsAnUnknownPolicy()
    {
        await Run(new ScriptedModel(), "/policy bogus");

        Assert.That(_app.Policy.Mode, Is.EqualTo("effect"));
        Assert.That(_app.Ui.AllOutput, Does.Contain("Unknown mode"));
    }

    [Test]
    public async Task RejectsAnUnknownCommandWithoutCallingTheModel()
    {
        var model = new ScriptedModel();

        await Run(model, "/dance");

        Assert.That(_app.Ui.AllOutput, Does.Contain("Unknown command"));
        Assert.That(model.Calls, Is.Zero);
    }

    [Test]
    public async Task ReportsAModelErrorAndKeepsGoing()
    {
        var model = new FailingThenAnsweringModel();

        await Run(model, "first", "second");

        Assert.That(_app.Ui.AllOutput, Does.Contain("Error: boom").And.Contain("recovered"));
    }

    [Test]
    public async Task ReportsACancelledTurnAndKeepsGoing()
    {
        var model = new CancellingThenAnsweringModel();

        await Run(model, "first", "second");

        Assert.That(_app.Ui.AllOutput, Does.Contain("Stopped.").And.Contain("recovered"));
    }

    private sealed class FailingThenAnsweringModel : ILanguageModel
    {
        private bool _failed;

        public Task<Message> CompleteAsync(IReadOnlyList<Message> conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation)
        {
            if (_failed) return Task.FromResult(Reply.Text("recovered"));
            _failed = true;
            throw new HttpRequestException("boom");
        }
    }

    private sealed class CancellingThenAnsweringModel : ILanguageModel
    {
        private bool _cancelled;

        public Task<Message> CompleteAsync(IReadOnlyList<Message> conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation)
        {
            if (_cancelled) return Task.FromResult(Reply.Text("recovered"));
            _cancelled = true;
            throw new OperationCanceledException();
        }
    }
}
