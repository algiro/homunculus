using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>grep and glob inside the whole application: approval rules, tool selection and the prompt.</summary>
public class SearchToolsAppTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model);
        _app.Project.Write("src/App.cs", ".Add(new ExitCommand());\n");
        return model;
    }

    [Test]
    public async Task TheModelCanFindFilesWithGlobWithoutAsking()
    {
        var model = Start(Replies.Tool("glob", """{"pattern":"*.cs"}"""), Replies.Text("found it"));

        await _app.Agent.RespondAsync("where is the app?", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(model.Conversations[1][^1].Content, Is.EqualTo("src/App.cs"));
    }

    [Test]
    public async Task AsksBeforeSearchingOutsideTheProject()
    {
        var model = Start(Replies.Tool("glob", """{"pattern":"*","path":"../.."}"""), Replies.Text("ok"));
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("OUTSIDE"));
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Refused by the user."));
    }

    [Test]
    public void CanBeChosenWithTheEnabledToolsSetting()
    {
        _app = new TestApp(new ScriptedModel(), settings: TestApp.DefaultSettings with { EnabledTools = ["glob"] });

        Assert.That(_app.Tools.All.Select(tool => tool.Name), Is.EquivalentTo(new[] { "glob" }));
    }

    [Test]
    public async Task TheSystemPromptTellsTheModelToFindFilesInsteadOfGuessingPaths()
    {
        var model = Start(Replies.Text("hi"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.LastConversation[0].Content, Does.Contain("with glob instead of guessing a path"));
    }

    [Test]
    public async Task GlobIsDescribedToTheModel()
    {
        var model = Start(Replies.Text("hi"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var specs = model.ToolLists[0];
        Assert.That(specs.Single(spec => spec.Name == "glob").Parameters["properties"]!.AsObject().Select(p => p.Key), Is.EqualTo(new[] { "pattern", "path" }));
    }
}
