using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>A file read twice, or changed after it was read, does not stay in the conversation in full.</summary>
public class StaleReadsTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        _app.Project.Write("A.cs", "alpha content");
        _app.Project.Write("B.cs", "beta content");
        return model;
    }

    private static Message Read(string path, string id) => Replies.Tool("read_file", $$"""{"path":"{{path}}"}""", id);

    private static string ToolResults(IReadOnlyList<Message> conversation) =>
        string.Join('\n', conversation.Where(message => message.Role == Role.Tool).Select(message => message.Content));

    [Test]
    public async Task LeavesOutTheEarlierCopyOfAFileThatWasReadAgain()
    {
        var model = Start(Read("A.cs", "c1"), Read("A.cs", "c2"), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var tools = model.LastConversation.Where(message => message.Role == Role.Tool).ToList();
        Assert.That(tools[0].Content, Is.EqualTo("[This earlier read of A.cs was left out: the file was read again further down.]"));
        Assert.That(tools[1].Content, Is.EqualTo("alpha content"));
        Assert.That(tools[0].ToolCallId, Is.EqualTo("c1"), "the answer stays attached to its call");
    }

    [Test]
    public async Task KeepsReadsOfDifferentFiles()
    {
        var model = Start(Read("A.cs", "c1"), Read("B.cs", "c2"), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(ToolResults(model.LastConversation), Is.EqualTo("alpha content\nbeta content"));
    }

    [Test]
    public async Task LeavesOutAReadOfAFileThatWasChangedAfterwards()
    {
        var model = Start(
            Read("A.cs", "c1"),
            Replies.Tool("edit_file", """{"path":"A.cs","old_str":"alpha","new_str":"gamma"}""", "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(ToolResults(model.LastConversation),
            Does.StartWith("[This earlier read of A.cs was left out: the file was changed afterwards, so this copy is out of date.]"));
    }

    [Test]
    public async Task LeavesOutAReadOfAFileThatWasInsertedInto()
    {
        var model = Start(
            Read("A.cs", "c1"),
            Replies.Tool("insert_lines", """{"path":"A.cs","line":"alpha content","text":"more"}""", "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(ToolResults(model.LastConversation), Does.StartWith("[This earlier read of A.cs was left out"));
    }

    [Test]
    public async Task KeepsTheReadWhenTheChangeFailed()
    {
        var model = Start(
            Read("A.cs", "c1"),
            Replies.Tool("edit_file", """{"path":"A.cs","old_str":"zzz","new_str":"x"}""", "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(ToolResults(model.LastConversation), Does.StartWith("alpha content"));
    }

    [Test]
    public async Task KeepsTheReadWhenTheFileIsReadAgainButTheSecondReadFails()
    {
        var model = Start(Read("A.cs", "c1"), Read("Missing.cs", "c2"), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(ToolResults(model.LastConversation), Does.StartWith("alpha content"));
    }

    [Test]
    public async Task OnlyTheLatestCopyIsKeptAfterSeveralReads()
    {
        var model = Start(Read("A.cs", "c1"), Read("A.cs", "c2"), Read("A.cs", "c3"), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var tools = model.LastConversation.Where(message => message.Role == Role.Tool).Select(message => message.Content).ToList();
        Assert.That(tools.Count(content => content == "alpha content"), Is.EqualTo(1));
        Assert.That(tools[^1], Is.EqualTo("alpha content"));
    }

    [Test]
    public async Task AReadAfterAChangeIsKept()
    {
        var model = Start(
            Replies.Tool("edit_file", """{"path":"A.cs","old_str":"alpha","new_str":"gamma"}""", "c1"),
            Read("A.cs", "c2"),
            Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(ToolResults(model.LastConversation), Does.EndWith("gamma content"));
    }

    [Test]
    public async Task ForgetsEverythingWhenTheConversationIsCleared()
    {
        var model = Start(Read("A.cs", "c1"), Replies.Text("read"), Read("A.cs", "c2"), Replies.Text("read again"));
        _app.Ui.TypesLines("first", "/clear", "second");

        await _app.Repl.RunAsync();

        Assert.That(ToolResults(model.LastConversation), Is.EqualTo("alpha content"));
    }

    [Test]
    public async Task SavesRoomInTheRequestSentToTheModel()
    {
        var big = new string('x', 5000);
        var model = Start(Read("A.cs", "c1"), Read("A.cs", "c2"), Read("A.cs", "c3"), Replies.Text("done"));
        _app.Project.Write("A.cs", big);

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var sent = model.LastConversation.Where(message => message.Role == Role.Tool).Sum(message => message.Content!.Length);
        Assert.That(sent, Is.LessThan(5000 + 400), "one full copy and two short notes, not three copies");
    }
}
