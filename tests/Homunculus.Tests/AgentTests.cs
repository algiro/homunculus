using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

public class AgentTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private TestApp Start(ScriptedModel model, string mode = "allow-all", Settings? settings = null) =>
        _app = new TestApp(model, mode, settings: settings);

    private Task Ask(string input = "do it") => _app.Agent.RespondAsync(input, CancellationToken.None);

    [Test]
    public async Task ShowsATextReplyAndStops()
    {
        Start(new ScriptedModel(Reply.Text("hello")));

        await Ask();

        Assert.That(_app.Ui.Output, Is.EqualTo(new[] { "hello" }));
    }

    [Test]
    public async Task SendsTheSystemPromptTheUserMessageAndAllTools()
    {
        var model = new ScriptedModel(Reply.Text("ok"));
        Start(model);

        await Ask("fix the bug");

        Assert.That(model.LastConversation.Select(m => m.Role), Is.EqualTo(new[] { Role.System, Role.User }));
        Assert.That(model.LastConversation[1].Content, Is.EqualTo("fix the bug"));
        Assert.That(model.ToolLists[0], Has.Count.EqualTo(6));
    }

    [Test]
    public async Task RunsARequestedToolAndReturnsTheResultToTheModel()
    {
        var model = new ScriptedModel(
            Reply.Tool("read_file", """{"path":"a.txt"}""", "c1"),
            Reply.Text("it says hi"));
        Start(model);
        _app.Project.Write("a.txt", "hi");

        await Ask();

        var toolResult = model.Conversations[1][^1];
        Assert.That(toolResult.Role, Is.EqualTo(Role.Tool));
        Assert.That(toolResult.ToolCallId, Is.EqualTo("c1"));
        Assert.That(toolResult.Content, Is.EqualTo("hi"));
        Assert.That(_app.Ui.Output[^1], Is.EqualTo("it says hi"));
    }

    [Test]
    public async Task AnnouncesEachToolCall()
    {
        Start(new ScriptedModel(Reply.Tool("list_files"), Reply.Text("done")));

        await Ask();

        Assert.That(_app.Ui.Output[0], Does.Contain("list_files"));
    }

    [Test]
    public async Task AnswersEveryToolCallOfOneReplyInOrder()
    {
        var model = new ScriptedModel(
            Reply.Calls(
                new ToolCall("a", "edit_file", Json.Parse("""{"path":"x.txt","old_str":"","new_str":"1"}""")),
                new ToolCall("b", "read_file", Json.Parse("""{"path":"x.txt"}"""))),
            Reply.Text("done"));
        Start(model);

        await Ask();

        var results = model.Conversations[1].Where(m => m.Role == Role.Tool).ToList();
        Assert.That(results.Select(r => r.ToolCallId), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(results[1].Content, Is.EqualTo("1"));
    }

    [Test]
    public async Task TellsTheModelWhenAToolIsUnknown()
    {
        var model = new ScriptedModel(Reply.Tool("fly"), Reply.Text("sorry"));
        Start(model);

        await Ask();

        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Error: unknown tool 'fly'"));
    }

    [Test]
    public async Task TurnsAToolFailureIntoAnErrorResultInsteadOfCrashing()
    {
        var model = new ScriptedModel(Reply.Tool("read_file", """{"path":"missing.txt"}"""), Reply.Text("ok"));
        Start(model);

        await Ask();

        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Error: File not found"));
    }

    [Test]
    public async Task TurnsAMissingArgumentIntoAnErrorResult()
    {
        var model = new ScriptedModel(Reply.Tool("read_file", "{}"), Reply.Text("ok"));
        Start(model);

        await Ask();

        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Error: Missing argument 'path'"));
    }

    [Test]
    public async Task DoesNotRunAToolTheUserRefuses()
    {
        var model = new ScriptedModel(
            Reply.Tool("edit_file", """{"path":"../outside.txt","old_str":"","new_str":"x"}"""),
            Reply.Text("understood"));
        Start(model, "effect");
        _app.Ui.Answers('n');

        await Ask();

        Assert.That(File.Exists(Path.Combine(_app.Project.Outside, "outside.txt")), Is.False);
        Assert.That(model.Conversations[1][^1].Content, Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task RunsAToolOutsideTheProjectWhenTheUserAgrees()
    {
        Start(new ScriptedModel(
            Reply.Tool("edit_file", """{"path":"../outside.txt","old_str":"","new_str":"x"}"""),
            Reply.Text("done")), "effect");
        _app.Ui.Answers('y');

        await Ask();

        Assert.That(File.Exists(Path.Combine(_app.Project.Outside, "outside.txt")), Is.True);
    }

    [Test]
    public async Task StopsAfterTheIterationLimit()
    {
        var model = ScriptedModel.Repeating(Reply.Tool("list_files"));
        Start(model);

        await Ask();

        Assert.That(model.Calls, Is.EqualTo(8));
        Assert.That(_app.Ui.Output[^1], Does.Contain("iteration limit"));
    }

    [Test]
    public async Task AllowsMoreIterationsOnceAPlanWasMade()
    {
        var script = new[] { Reply.Tool("plan", """{"steps":["a"]}""") }
            .Concat(Enumerable.Repeat(Reply.Tool("list_files"), 40))
            .ToArray();
        var model = new ScriptedModel(script);
        Start(model);

        await Ask();

        Assert.That(model.Calls, Is.EqualTo(24));
    }

    [Test]
    public async Task HonoursTheConfiguredIterationLimit()
    {
        var model = ScriptedModel.Repeating(Reply.Tool("list_files"));
        Start(model, settings: TestApp.DefaultSettings with { MaxIterations = 2 });

        await Ask();

        Assert.That(model.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task RemembersTheConversationBetweenMessages()
    {
        var model = new ScriptedModel(Reply.Text("first"), Reply.Text("second"));
        Start(model);

        await Ask("one");
        await Ask("two");

        Assert.That(model.LastConversation.Select(m => m.Content), Is.EqualTo(new[] { model.LastConversation[0].Content, "one", "first", "two" }));
    }

    [Test]
    public async Task ForgetsTheConversationWhenCleared()
    {
        var model = new ScriptedModel(Reply.Text("first"), Reply.Text("second"));
        Start(model);

        await Ask("one");
        _app.Agent.Clear();
        await Ask("two");

        Assert.That(model.LastConversation.Select(m => m.Role), Is.EqualTo(new[] { Role.System, Role.User }));
        Assert.That(model.LastConversation[1].Content, Is.EqualTo("two"));
    }

    [Test]
    public void PropagatesCancellation()
    {
        Start(new ScriptedModel(Reply.Text("never")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => _app.Agent.RespondAsync("x", cancellation.Token));
    }
}
