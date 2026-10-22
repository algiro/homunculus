using Homunculus.Commands;
using Homunculus.Llm;
using Homunculus.Tests.Support;
using NSubstitute;

namespace Homunculus.Tests;

/// <summary>Whole-application behaviour: the pieces working together.</summary>
public class AppTests
{
    private const string RunEcho = """{"command":"echo hi"}""";

    private TestApp _app = null!;

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
        _app = null!;
    }

    private async Task<ScriptedModel> RunCommandWithJudge(string judgeAnswer, params char[] userAnswers)
    {
        var model = new ScriptedModel(Replies.Tool("run_command", RunEcho), Replies.Text("done"));
        _app = new TestApp(model, "judge", new ScriptedModel(Replies.Text(judgeAnswer)));
        _app.Ui.Answers(userAnswers);

        await _app.Agent.RespondAsync("go", CancellationToken.None);
        return model;
    }

    [Test]
    public async Task JudgeModeRunsACommandTheJudgeAllows()
    {
        var model = await RunCommandWithJudge("allow");

        Assert.That(model.Conversations[1][^1].Content, Does.Contain("hi").And.Contain("[exit code 0]"));
        Assert.That(_app.Ui.Questions, Is.Empty);
    }

    [Test]
    public async Task JudgeModeStopsACommandTheJudgeDenies()
    {
        var model = await RunCommandWithJudge("deny");

        Assert.That(model.Conversations[1][^1].Content, Is.EqualTo("Denied by the policy."));
        Assert.That(_app.Ui.Questions, Is.Empty);
    }

    [Test]
    public async Task JudgeModeAsksTheUserWhenTheJudgeIsUnsure()
    {
        var model = await RunCommandWithJudge("ask", 'y');

        Assert.That(_app.Ui.Questions, Has.Count.EqualTo(1));
        Assert.That(model.Conversations[1][^1].Content, Does.Contain("[exit code 0]"));
    }

    [Test]
    public async Task EffectModeIsTheOnlyOneThatNeverConsultsTheJudge()
    {
        var judge = new ScriptedModel();
        _app = new TestApp(new ScriptedModel(Replies.Tool("run_command", RunEcho), Replies.Text("done")), "effect", judge);
        _app.Ui.Answers('y');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(judge.Calls, Is.Zero);
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
    public async Task UsageCountsTheAgentAndTheJudgeSeparately()
    {
        var model = new ScriptedModel(Replies.Tool("run_command", RunEcho), Replies.Text("done"))
        {
            Usage = new TokenUsage(10, 5, 15),
        };
        var judge = new ScriptedModel(Replies.Text("allow")) { Usage = new TokenUsage(3, 1, 4, Cached: 2) };
        _app = new TestApp(model, "judge", judge);
        _app.Ui.TypesLines("go", "/usage");

        await _app.Repl.RunAsync();

        Assert.That(_app.Ui.Output[^1], Is.EqualTo(string.Join('\n',
            "agent: 2 calls, 20 in, 10 out, 30 total",
            "judge: 1 call, 3 in (2 cached), 1 out, 4 total",
            "total: 3 calls, 23 in (2 cached), 11 out, 34 total")));
    }

    [Test]
    public async Task DoesNotWriteOutsideTheProjectWhenTheUserRefuses()
    {
        var model = new ScriptedModel(
            Replies.Tool("edit_file", """{"path":"../outside.txt","old_str":"","new_str":"x"}"""),
            Replies.Text("understood"));
        _app = new TestApp(model, "effect");
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(_app.Project.Outside, "outside.txt")), Is.False);
    }

    [Test]
    public async Task WritesOutsideTheProjectWhenTheUserAgrees()
    {
        var model = new ScriptedModel(
            Replies.Tool("edit_file", """{"path":"../outside.txt","old_str":"","new_str":"x"}"""),
            Replies.Text("done"));
        _app = new TestApp(model, "effect");
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

        Assert.That(_app.Ui.AllOutput, Does.Contain("/help").And.Contain("/tools").And.Contain("/usage").And.Contain("/policy [mode]")
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
