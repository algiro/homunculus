using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>grep and glob inside the whole application: approval rules, tool selection and the prompt.</summary>
public class SearchToolsAppTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ScriptedModel Start(string mode, params Message[] script)
    {
        var model = new ScriptedModel(script);
        _app = new TestApp(model, mode);
        _app.Project.Write("src/App.cs", ".Add(new ExitCommand());\n");
        return model;
    }

    [Test]
    public async Task TheModelCanSearchTheProjectWithGrepAndGlobWithoutAsking()
    {
        var model = Start("effect",
            Replies.Tool("grep", """{"pattern":"ExitCommand"}""", "c1"),
            Replies.Tool("glob", """{"pattern":"*.cs"}""", "c2"),
            Replies.Text("found it"));

        await _app.Agent.RespondAsync("where is the exit command registered?", CancellationToken.None);

        Assert.That(_app.Ui.Questions, Is.Empty);
        Assert.That(model.Conversations[1][^1].Content, Is.EqualTo("src/App.cs:1: .Add(new ExitCommand());"));
        Assert.That(model.Conversations[2][^1].Content, Is.EqualTo("src/App.cs"));
    }

    [TestCase("grep", """{"pattern":"x","path":"../.."}""")]
    [TestCase("glob", """{"pattern":"*","path":"../.."}""")]
    public async Task AsksBeforeSearchingOutsideTheProjectInEveryMode(string tool, string arguments)
    {
        var model = Start("allow-all", Replies.Tool(tool, arguments), Replies.Text("ok"));
        _app.Ui.Answers('n');

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(_app.Ui.Questions.Single(), Does.Contain("OUTSIDE"));
        Assert.That(model.Conversations[1][^1].Content, Does.StartWith("Refused by the user."));
    }

    [Test]
    public void CanBeChosenWithTheEnabledToolsSetting()
    {
        _app = new TestApp(new ScriptedModel(), settings: TestApp.DefaultSettings with { EnabledTools = ["grep", "glob"] });

        Assert.That(_app.Tools.All.Select(tool => tool.Name), Is.EquivalentTo(new[] { "grep", "glob", "plan" }));
    }

    [Test]
    public async Task TheSystemPromptTellsTheModelToSearchInsteadOfGuessingPaths()
    {
        var model = Start("allow-all", Replies.Text("hi"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.LastConversation[0].Content, Does.Contain("search with grep and glob").And.Contain("instead of guessing a path"));
    }

    [Test]
    public async Task BothToolsAreDescribedToTheModel()
    {
        var model = Start("allow-all", Replies.Text("hi"));

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        var specs = model.ToolLists[0];
        Assert.That(specs.Single(spec => spec.Name == "grep").Parameters["required"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "pattern" }));
        Assert.That(specs.Single(spec => spec.Name == "glob").Parameters["properties"]!.AsObject().Select(p => p.Key), Is.EqualTo(new[] { "pattern", "path" }));
    }
}
