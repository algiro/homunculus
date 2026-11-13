using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

public class ReplTests
{
    private TestApp _app = null!;

    private async Task Run(ILanguageModel model, params string[] lines)
    {
        _app = new TestApp(model);
        _app.Ui.TypesLines(lines);
        await _app.Repl.RunAsync();
    }

    [Test]
    public async Task SendsPlainTextToTheAgent()
    {
        var model = new ScriptedModel(Replies.Text("hello there"));

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
        var model = new ScriptedModel(Replies.Text("never"));

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

        Assert.That(_app.Ui.AllOutput, Does.Contain("/exit").And.Contain("/usage"));
    }

    [Test]
    public async Task ClearsTheConversation()
    {
        var model = new ScriptedModel(Replies.Text("one"), Replies.Text("two"));

        await Run(model, "first", "/clear", "second");

        Assert.That(_app.Ui.AllOutput, Does.Contain("Conversation cleared."));
        Assert.That(model.LastConversation.Select(m => m.Role), Is.EqualTo(new[] { Role.System, Role.User }));
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

        public Task<Reply> CompleteAsync(IConversation conversation, CancellationToken cancellation)
        {
            if (_failed) return Task.FromResult(new Reply(Replies.Text("recovered")));
            _failed = true;
            throw new ModelException("boom");
        }
    }

    private sealed class CancellingThenAnsweringModel : ILanguageModel
    {
        private bool _cancelled;

        public Task<Reply> CompleteAsync(IConversation conversation, CancellationToken cancellation)
        {
            if (_cancelled) return Task.FromResult(new Reply(Replies.Text("recovered")));
            _cancelled = true;
            throw new OperationCanceledException();
        }
    }
}
