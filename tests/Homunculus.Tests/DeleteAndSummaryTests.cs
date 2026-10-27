using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>Whole-application behaviour around deleting files and telling the user what a turn changed.</summary>
public class DeleteAndSummaryTests
{
    private const string DeleteApp = """{"path":"App.cs"}""";

    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(string mode, params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model, mode);
        return model;
    }

    private static Message Create(string path, string id = "c1") =>
        Replies.Tool("edit_file", $$"""{"path":"{{path}}","old_str":"","new_str":"class A {}"}""", id);

    [TestCase("allow-all")]
    [TestCase("effect")]
    public async Task AsksBeforeDeletingAFileTheAgentDidNotCreateInEveryMode(string mode)
    {
        var model = Start(mode, Replies.Tool("delete_file", DeleteApp), Replies.Text("ok"));
        _app.Project.Write("App.cs", "keep me");
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("would delete App.cs").And.Contain("did not create"));
        Assert.That(_app.Project.Exists("App.cs"), Is.True);
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Refused by the user."));
    }

    [Test]
    public async Task DeletesThatFileWhenTheUserAgrees()
    {
        Start("allow-all", Replies.Tool("delete_file", DeleteApp), Replies.Text("ok"));
        _app.Project.Write("App.cs", "bye");
        _app.Ui.Answers('y');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Project.Exists("App.cs"), Is.False);
    }

    [Test]
    public async Task DoesNotRememberTheUsersOkForTheNextDeletion()
    {
        Start("allow-all",
            Replies.Tool("delete_file", """{"path":"A.cs"}""", "c1"),
            Replies.Tool("delete_file", """{"path":"B.cs"}""", "c2"),
            Replies.Text("ok"));
        _app.Project.Write("A.cs", "a");
        _app.Project.Write("B.cs", "b");
        _app.Ui.Answers('y', 'n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Has.Count.EqualTo(2));
        Assert.That(_app.Project.Exists("B.cs"), Is.True);
    }

    [Test]
    public async Task DeletesAFileTheAgentCreatedItselfWithoutAsking()
    {
        Start("allow-all", Create("Scratch.cs"), Replies.Tool("delete_file", """{"path":"Scratch.cs"}""", "c2"), Replies.Text("ok"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(_app.Project.Exists("Scratch.cs"), Is.False);
    }

    [Test]
    public async Task StillAsksAboutAFileCreatedByTheAgentInAnotherSessionOfTheSameName()
    {
        Start("allow-all", Replies.Tool("delete_file", """{"path":"Existing.cs"}"""), Replies.Text("ok"));
        _app.Project.Write("Existing.cs", "x");
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ListsTheChangedFilesAtTheEndOfTheTurn()
    {
        Start("allow-all", Create("New.cs"), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Output[^1], Is.EqualTo("Changed in this turn:\n  created New.cs"));
    }

    [Test]
    public async Task ListsNothingWhenNoFileChanged()
    {
        Start("allow-all", Replies.Text("just an answer"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("Changed in this turn"));
    }

    [Test]
    public async Task ListsOnlyWhatTheLatestTurnChanged()
    {
        Start("allow-all", Create("One.cs"), Replies.Text("first"), Create("Two.cs", "c2"), Replies.Text("second"));

        await _app.Agent.RespondAsync("one", CancellationToken.None);
        await _app.Agent.RespondAsync("two", CancellationToken.None);

        Assert.That(_app.Ui.Output[^1], Is.EqualTo("Changed in this turn:\n  created Two.cs"));
    }

    [Test]
    public async Task ListsTheChangesEvenWhenTheTurnIsStoppedForRepeatedFailures()
    {
        Start("allow-all",
            Create("New.cs", "c0"),
            Replies.Tool("read_file", """{"path":"nope.txt"}""", "c1"),
            Replies.Tool("read_file", """{"path":"nope.txt"}""", "c2"),
            Replies.Tool("read_file", """{"path":"nope.txt"}""", "c3"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Output[^2], Does.StartWith("Stopped:"));
        Assert.That(_app.Ui.Output[^1], Is.EqualTo("Changed in this turn:\n  created New.cs"));
    }

    [Test]
    public async Task ListsTheChangesWhenTheUserStopsTheTurn()
    {
        var model = new ScriptedModel(Create("New.cs"));
        _app = new TestApp(new CancellingAfter(model));

        await _app.Agent.RespondAsync("go", CancellationToken.None).ContinueWith(_ => { });

        Assert.That(_app.Ui.Output[^1], Is.EqualTo("Changed in this turn:\n  created New.cs"));
    }

    [Test]
    public async Task TellsTheModelNotToBreakExistingCodeToMakeItsOwnCompile()
    {
        var model = Start("allow-all", Replies.Text("hi"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var prompt = model.LastConversation[0].Content!;
        Assert.That(prompt, Does.Contain("read it and one existing implementation").And.Contain("never change or delete existing files or types"));
    }

    /// <summary>Answers from the wrapped model, then cancels the call that follows, as ctrl-c would.</summary>
    private sealed class CancellingAfter(ScriptedModel inner) : ILanguageModel
    {
        public async Task<Reply> CompleteAsync(IConversation conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation)
        {
            if (inner.Calls == 1) throw new OperationCanceledException();
            return await inner.CompleteAsync(conversation, tools, cancellation);
        }
    }
}
