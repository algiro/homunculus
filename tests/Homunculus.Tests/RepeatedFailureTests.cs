using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>A model that repeats the same failing call is told so, and then stopped.</summary>
public class RepeatedFailureTests
{
    private const string Missing = """{"path":"nope.txt"}""";

    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        return model;
    }

    private Task<string> Run(string tool, string json) =>
        _app.Runner.RunAsync(new ToolCall("c1", tool, Json.Parse(json)), CancellationToken.None);

    [Test]
    public async Task TheFirstFailureCarriesNoWarning()
    {
        Start();

        Assert.That(await Run("read_file", Missing), Does.Not.Contain("already made"));
    }

    [Test]
    public async Task TheSecondIdenticalFailureWarnsTheModel()
    {
        Start();
        await Run("read_file", Missing);

        Assert.That(await Run("read_file", Missing), Does.Contain("You already made this exact call").And.StartWith("Error: File not found"));
    }

    [Test]
    public async Task ADifferentCallIsNotARepeat()
    {
        Start();
        await Run("read_file", Missing);

        Assert.That(await Run("read_file", """{"path":"other.txt"}"""), Does.Not.Contain("already made"));
    }

    [Test]
    public async Task TheOrderOfTheArgumentsDoesNotMatter()
    {
        Start();
        await Run("edit_file", """{"path":"nope.txt","old_str":"a","new_str":"b"}""");

        Assert.That(await Run("edit_file", """{"new_str":"b","old_str":"a","path":"nope.txt"}"""), Does.Contain("already made"));
    }

    [Test]
    public async Task ASuccessForgetsTheEarlierFailures()
    {
        Start();
        await Run("read_file", Missing);
        _app.Project.Write("nope.txt", "now it exists");
        await Run("read_file", Missing);
        File.Delete(_app.Project.PathOf("nope.txt"));

        Assert.That(await Run("read_file", Missing), Does.Not.Contain("already made"));
    }

    [Test]
    public async Task UnknownToolsCountToo()
    {
        Start();
        await Run("fly", "{}");

        Assert.That(await Run("fly", "{}"), Does.Contain("already made"));
    }

    [Test]
    public async Task IsStuckOnlyAfterTheThirdIdenticalFailure()
    {
        Start();
        var call = new ToolCall("c1", "read_file", Json.Parse(Missing));

        await Run("read_file", Missing);
        await Run("read_file", Missing);
        Assert.That(_app.Runner.IsStuckOn(call), Is.False);

        await Run("read_file", Missing);
        Assert.That(_app.Runner.IsStuckOn(call), Is.True);
    }

    [Test]
    public async Task TheAgentStopsTheTurnAfterTheThirdIdenticalFailureAndSaysWhy()
    {
        var model = Start(
            Replies.Tool("read_file", Missing, "c1"),
            Replies.Tool("read_file", Missing, "c2"),
            Replies.Tool("read_file", Missing, "c3"),
            Replies.Text("never reached"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Calls, Is.EqualTo(3));
        Assert.That(_app.Ui.Output[^1], Does.StartWith("Stopped: the model made the same read_file call 3 times and it failed or was refused each time"));
    }

    [Test]
    public async Task TheStoppedTurnLeavesAConversationTheNextMessageCanContinue()
    {
        var model = Start(
            Replies.Tool("read_file", Missing, "c1"),
            Replies.Tool("read_file", Missing, "c2"),
            Replies.Tool("read_file", Missing, "c3"),
            Replies.Text("understood"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);
        await _app.Agent.RespondAsync("create it instead", CancellationToken.None);

        Assert.That(_app.Ui.Output[^1], Is.EqualTo("understood"));
        Assert.That(model.LastConversation.Count(message => message.Role == Role.Tool), Is.EqualTo(3));
    }

    [Test]
    public async Task ANewMessageStartsCountingAgain()
    {
        var model = Start(
            Replies.Tool("read_file", Missing, "c1"),
            Replies.Tool("read_file", Missing, "c2"),
            Replies.Text("gave up"),
            Replies.Tool("read_file", Missing, "c3"),
            Replies.Text("ok"));

        await _app.Agent.RespondAsync("one", CancellationToken.None);
        await _app.Agent.RespondAsync("two", CancellationToken.None);

        Assert.That(model.Calls, Is.EqualTo(5));
        Assert.That(_app.Ui.Output, Has.None.StartWith("Stopped:"));
    }

    [Test]
    public async Task TheAgentAlsoStopsWhenTheUserRefusesTheSameCallThreeTimes()
    {
        var model = Start(
            Replies.Tool("run_command", """{"command":"dotnet test"}""", "c1"),
            Replies.Tool("run_command", """{"command":"dotnet test"}""", "c2"),
            Replies.Tool("run_command", """{"command":"dotnet test"}""", "c3"),
            Replies.Text("never reached"));
        _app.Policy.TrySetMode("effect");
        _app.Ui.Answers('n', 'n', 'n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Calls, Is.EqualTo(3));
        Assert.That(_app.Ui.Output[^1], Does.StartWith("Stopped: the model made the same run_command call 3 times"));
    }
}
