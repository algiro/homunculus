using Homunculus.Commands;
using Homunculus.Llm;
using Homunculus.Tests.Support;
using NSubstitute;

namespace Homunculus.Tests;

/// <summary>Whole-application behaviour: the pieces working together.</summary>
public class AppTests
{
    private TestApp _app = null!;

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
        _app = null!;
    }

    [Test]
    public async Task UsageSaysSoWhenNothingWasReported()
    {
        _app = new TestApp(new ScriptedModel());
        _app.Ui.TypesLines("/usage");

        await _app.Repl.RunAsync();

        Assert.That(_app.Ui.AllOutput, Does.Contain("No tokens reported yet."));
    }

    [Test]
    public async Task DoesNotWriteOutsideTheProjectWhenTheUserRefuses()
    {
        var model = new ScriptedModel(
            Replies.Tool("edit_file", """{"path":"../outside.txt","old_str":"","new_str":"x"}"""),
            Replies.Text("understood"));
        _app = new TestApp(model);
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(_app.Project.Outside, "outside.txt")), Is.False);
    }

    [Test]
    public async Task AsksBeforeReadingOutsideTheProject()
    {
        var model = new ScriptedModel(
            Replies.Tool("list_files", """{"path":"/"}"""),
            Replies.Text("understood"));
        _app = new TestApp(model);
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("OUTSIDE"));
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Refused by the user."));
    }

    [Test]
    public void TellsTheModelWhereTheProjectIs()
    {
        var model = new ScriptedModel(Replies.Text("hi"));
        _app = new TestApp(model);

        _app.Agent.RespondAsync("go", CancellationToken.None).GetAwaiter().GetResult();

        Assert.That(model.LastConversation[0].Content, Does.Contain(_app.Project.Location.Root));
    }

    [Test]
    public async Task WritesOutsideTheProjectWhenTheUserAgrees()
    {
        var model = new ScriptedModel(
            Replies.Tool("edit_file", """{"path":"../outside.txt","old_str":"","new_str":"x"}"""),
            Replies.Text("done"));
        _app = new TestApp(model);
        _app.Ui.Answers('y');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(_app.Project.Outside, "outside.txt")), Is.True);
    }

    [Test]
    public async Task TheUserCanStopATurnThatIsWaitingForTheModel()
    {
        var model = new BlockingModel();
        _app = new TestApp(model);
        _app.Ui.TypesLines("slow question");

        var running = _app.Repl.RunAsync();
        await model.Started;
        _app.Ui.PressCancel();
        await running;

        Assert.That(_app.Ui.AllOutput, Does.Contain("Stopped."));
    }

    [Test]
    public async Task ExitStopsTheLoopSoLaterLinesAreIgnored()
    {
        var model = new ScriptedModel();
        _app = new TestApp(model);
        _app.Ui.TypesLines("/exit", "hello");

        await _app.Repl.RunAsync();

        Assert.That(model.Calls, Is.Zero);
    }

    [Test]
    public async Task HelpListsEveryRegisteredCommand()
    {
        _app = new TestApp(new ScriptedModel());
        _app.Ui.TypesLines("/help");

        await _app.Repl.RunAsync();

        Assert.That(_app.Ui.AllOutput, Does.Contain("/help").And.Contain("/tools").And.Contain("/usage")
            .And.Contain("/clear").And.Contain("/exit").And.Contain("ctrl-c"));
    }

    [Test]
    public void RegistryFindsCommandsByNameAndDispatchesTheArgument()
    {
        var command = Substitute.For<ICommand>();
        command.Name.Returns("/echo");
        var registry = new CommandRegistry().Add(command);

        Assert.That(registry.Find("/echo"), Is.SameAs(command));
        Assert.That(registry.Find("/other"), Is.Null);
    }

    [Test]
    public async Task ReplPassesTheTextAfterTheCommandNameToTheCommand()
    {
        var command = Substitute.For<ICommand>();
        command.Name.Returns("/echo");
        var ui = new FakeUserInterface().TypesLines("/echo  some words ", "/echo");
        _app = new TestApp(new ScriptedModel());

        await new Repl(_app.Agent, new CommandRegistry().Add(command), ui).RunAsync();

        Received.InOrder(() =>
        {
            command.Execute("some words");
            command.Execute("");
        });
    }

    private sealed class BlockingModel : ILanguageModel
    {
        private readonly TaskCompletionSource _started = new();

        public Task Started => _started.Task;

        public async Task<Reply> CompleteAsync(IConversation conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation)
        {
            _started.SetResult();
            await Task.Delay(Timeout.Infinite, cancellation);
            return new Reply(Replies.Text("unreachable"));
        }
    }
}
