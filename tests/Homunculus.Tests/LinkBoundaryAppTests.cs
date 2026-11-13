using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>
/// A link inside the project that leads outside it (a symbolic link in a cloned repository, or a junction any Windows user can
/// make): every tool must treat what it leads to as outside, and searches must not walk through it.
/// </summary>
public class LinkBoundaryAppTests
{
    private TestApp _app = null!;
    private string _secrets = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        _app.Project.Write("README.md", "a project");
        _secrets = Path.Combine(_app.Project.Outside, "secrets");
        Directory.CreateDirectory(_secrets);
        File.WriteAllText(Path.Combine(_secrets, "passwd"), "root:x:0:0");
        _app.Project.LinkDirectory("link", _secrets);
        return model;
    }

    [Test]
    public async Task AsksBeforeReadingThroughALinkThatLeadsOutside()
    {
        var model = Start(Replies.Tool("read_file", """{"path":"link/passwd"}"""), Replies.Text("understood"));
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("OUTSIDE"));
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Refused by the user."));
    }

    [Test]
    public async Task DoesNotWriteThroughALinkThatLeadsOutsideWhenTheUserRefuses()
    {
        Start(Replies.Tool("edit_file", """{"path":"link/passwd","old_str":"root","new_str":"pwned"}"""), Replies.Text("understood"));
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("OUTSIDE"));
        Assert.That(File.ReadAllText(Path.Combine(_secrets, "passwd")), Is.EqualTo("root:x:0:0"));
    }

    [Test]
    public async Task ListingTheProjectRootWithALinkInItDoesNotAsk()
    {
        var model = Start(Replies.Tool("list_files", "{}"), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(model.Conversations[1][^1].Content, Does.Contain("link/"));
    }

    [Test]
    public async Task GlobDoesNotFollowALinkOutOfTheProject()
    {
        var model = Start(Replies.Tool("glob", """{"pattern":"*"}"""), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(model.Conversations[1][^1].Content, Does.Contain("README.md").And.Not.Contain("passwd"));
    }

    [Test]
    public async Task GrepDoesNotFollowALinkOutOfTheProject()
    {
        var model = Start(Replies.Tool("grep", """{"pattern":"root"}"""), Replies.Text("done"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(model.Conversations[1][^1].Content, Does.Not.Contain("passwd").And.Not.Contain("root:x"));
    }
}
