using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>insert_lines inside the whole application.</summary>
public class InsertLinesAppTests
{
    private const string Insert =
        """{"path":"App.cs","line":".Add(new ClearCommand(agent, ui))","text":".Add(new VersionCommand())"}""";

    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        _app.Project.Write("App.cs", "commands\n    .Add(new ClearCommand(agent, ui))\n    .Add(new ExitCommand());\n");
        return model;
    }

    [Test]
    public async Task RegistersSomethingNextToItsSiblingsWithoutAsking()
    {
        var model = Start(Replies.Tool("insert_lines", Insert), Replies.Text("done"));

        await _app.Agent.RespondAsync("register it", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(_app.Project.Read("App.cs"), Does.Contain(".Add(new ClearCommand(agent, ui))\n    .Add(new VersionCommand())\n    .Add(new ExitCommand());"));
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Inserted 1 line after line 2 of App.cs:"));
    }

    [Test]
    public async Task ListsTheFileAsEditedAtTheEndOfTheTurn()
    {
        Start(Replies.Tool("insert_lines", Insert), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Output[^1], Is.EqualTo("Changed in this turn:\n  edited App.cs"));
    }

    [Test]
    public async Task AsksBeforeChangingAFileOutsideTheProject()
    {
        var model = Start(Replies.Tool("insert_lines", """{"path":"../outside.txt","line":"a","text":"b"}"""),
            Replies.Text("ok"));
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("OUTSIDE"));
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Refused by the user."));
    }

    [Test]
    public async Task IsDescribedToTheModelAndMentionedInThePromptAndInEditFile()
    {
        var model = Start(Replies.Text("hi"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var specs = model.ToolLists[0];
        Assert.That(specs.Single(spec => spec.Name == "insert_lines").Parameters["required"]!.AsArray().Select(n => n!.GetValue<string>()),
            Is.EqualTo(new[] { "path", "line", "text" }));
        Assert.That(model.LastConversation[0].Content, Does.Contain("use insert_lines, not a rewritten block"));
        Assert.That(specs.Single(spec => spec.Name == "edit_file").Description, Does.Contain("insert_lines is simpler"));
    }

    [Test]
    public async Task RepeatedFailingInsertionsAreStoppedLikeAnyOtherToolCall()
    {
        var bad = """{"path":"App.cs","line":"zzz_missing()","text":"x"}""";
        var model = Start(Replies.Tool("insert_lines", bad, "c1"), Replies.Tool("insert_lines", bad, "c2"), Replies.Tool("insert_lines", bad, "c3"), Replies.Text("never"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Calls, Is.EqualTo(3));
        Assert.That(_app.Ui.AllOutput, Does.Contain("Stopped: the model made the same insert_lines call 3 times"));
    }
}
