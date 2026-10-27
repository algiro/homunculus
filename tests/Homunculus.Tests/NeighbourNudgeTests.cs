using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>The reminder to read a neighbour, seen through the whole application.</summary>
public class NeighbourNudgeTests
{
    private const string CreateNew = """{"path":"Commands/New.cs","old_str":"","new_str":"class New {}"}""";

    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        _app.Project.Write("Commands/ICommand.cs", "interface");
        return model;
    }

    [Test]
    public async Task TheModelIsRemindedWhenItCreatesAFileWithoutReadingANeighbour()
    {
        var model = Start(Replies.Tool("edit_file", CreateNew), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Conversations[1][^1].Content, Does.Contain("You have not read any other file in Commands"));
    }

    [Test]
    public async Task TheModelIsNotRemindedAfterReadingANeighbour()
    {
        var model = Start(
            Replies.Tool("read_file", """{"path":"Commands/ICommand.cs"}""", "c1"),
            Replies.Tool("edit_file", CreateNew, "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Conversations[2][^1].Content, Does.Not.Contain("You have not read"));
    }

    [Test]
    public async Task ClearingTheConversationMakesTheModelForgetWhatItRead()
    {
        var model = Start(
            Replies.Tool("read_file", """{"path":"Commands/ICommand.cs"}""", "c1"),
            Replies.Text("read it"),
            Replies.Tool("edit_file", CreateNew, "c2"),
            Replies.Text("done"));
        _app.Ui.TypesLines("look", "/clear", "now create it");

        await _app.Repl.RunAsync();

        Assert.That(model.Conversations[3][^1].Content, Does.Contain("You have not read any other file in Commands"));
    }
}
