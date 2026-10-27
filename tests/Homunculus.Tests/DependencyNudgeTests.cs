using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>The reminder about unread dependencies, seen through the whole application.</summary>
public class DependencyNudgeTests
{
    private const string CreateCommand =
        """{"path":"Commands/New.cs","old_str":"","new_str":"public sealed class New : ICommand {}"}""";

    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        _app.Project.Write("Commands/ICommand.cs", "interface ICommand {}");
        _app.Project.Write("Commands/CommandRegistry.cs", "class CommandRegistry {}");
        return model;
    }

    [Test]
    public async Task ReadingAnUnrelatedNeighbourDoesNotSilenceTheReminderAboutTheInterface()
    {
        var model = Start(
            Replies.Tool("read_file", """{"path":"Commands/CommandRegistry.cs"}""", "c1"),
            Replies.Tool("edit_file", CreateCommand, "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Conversations[2][^1].Content, Does.Contain("It uses ICommand, defined in Commands/ICommand.cs, which you have not read"));
    }

    [Test]
    public async Task ReadingTheInterfaceSilencesIt()
    {
        var model = Start(
            Replies.Tool("read_file", """{"path":"Commands/ICommand.cs"}""", "c1"),
            Replies.Tool("edit_file", CreateCommand, "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Conversations[2][^1].Content, Does.Not.Contain("not read"));
    }
}
