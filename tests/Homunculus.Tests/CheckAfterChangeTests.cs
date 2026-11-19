using Homunculus.Llm;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

/// <summary>The user's check command, run after every change to a file, seen through the whole application.</summary>
public class CheckAfterChangeTests
{
    private const string Create = """{"path":"New.cs","old_str":"","new_str":"class New {}"}""";

    private TestApp _app = null!;

    [TearDown]
    public void TearDown()
    {
        _app?.Dispose();
        _app = null!;
    }

    // Checks run without a shell, so they are real programs: dotnet is always there, and an unknown command makes it exit with 1.
    private const string Passing = "dotnet --version";
    private const string Failing = "dotnet homunculus-no-such-command";

    private void Start(string? check, string mode = "allow-all")
    {
        _app = new TestApp(new ScriptedModel(), mode, settings: TestApp.DefaultSettings with { CheckCommand = check });
        _app.Project.Write("Old.cs", "old");
    }

    private Task<string> Run(string tool, string json) =>
        _app.Runner.RunAsync(new ToolCall("c1", tool, Json.Parse(json)), CancellationToken.None);

    [Test]
    public async Task AddsThePassingVerdictToTheResultOfAChange()
    {
        Start(Passing);

        var result = await Run("edit_file", Create);

        Assert.That(result, Does.StartWith("Created").And.EndWith("\n\nCheck passed."));
        Assert.That(_app.Ui.ToolResults[^1].Summary, Does.EndWith(" · check passed"));
    }

    [Test]
    public async Task AddsTheFailureToTheResultOfAChange()
    {
        Start(Failing);

        var result = await Run("edit_file", Create);

        Assert.That(result, Does.StartWith("Created").And.Contain($"\n\nCheck `{Failing}` failed (exit code 1):"));
        Assert.That(_app.Ui.ToolResults[^1], Has.Property("Failed").True.And.Property("Summary").EndsWith(" · check failed"));
    }

    [Test]
    public async Task ChecksAfterEveryKindOfChange()
    {
        Start(Passing);
        _app.Ui.Answers('y');

        var edited = await Run("edit_file", """{"path":"Old.cs","old_str":"old","new_str":"new"}""");
        var inserted = await Run("insert_lines", """{"path":"Old.cs","line":"new","text":"more"}""");
        var deleted = await Run("delete_file", """{"path":"Old.cs"}""");

        Assert.That(new[] { edited, inserted, deleted }, Has.All.EndWith("Check passed."));
    }

    [Test]
    public async Task DoesNotCheckAfterReadingSearchingPlanningOrRunningACommand()
    {
        Start(Failing);

        var results = new[]
        {
            await Run("read_file", """{"path":"Old.cs"}"""),
            await Run("list_files", "{}"),
            await Run("grep", """{"pattern":"old"}"""),
            await Run("glob", """{"pattern":"*.cs"}"""),
            await Run("plan", """{"steps":["a"]}"""),
            await Run("run_command", """{"program":"dotnet","args":["--version"]}"""),
        };

        Assert.That(results, Has.None.Contain("Check"));
        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("check:"));
    }

    [Test]
    public async Task DoesNotCheckAChangeThatFailed()
    {
        Start(Failing);

        var result = await Run("edit_file", """{"path":"Old.cs","old_str":"zzz","new_str":"x"}""");

        Assert.That(result, Does.StartWith("Error: old_str was not found").And.Not.Contain("Check"));
    }

    [Test]
    public async Task DoesNotCheckAChangeTheUserRefused()
    {
        Start(Failing, "effect");
        _app.Ui.Answers('n');

        var result = await Run("delete_file", """{"path":"Old.cs"}""");

        Assert.That(result, Does.StartWith("Refused by the user.").And.Not.Contain("Check"));
    }

    [Test]
    public async Task DoesNothingWithoutACheckCommand()
    {
        Start(null);

        var result = await Run("edit_file", Create);

        Assert.That(result, Does.Not.Contain("Check"));
        Assert.That(_app.Ui.AllOutput, Does.Not.Contain("check:"));
    }

    [Test]
    public async Task TheModelReadsTheVerdictInTheConversation()
    {
        var model = new ScriptedModel(Replies.Tool("edit_file", Create), Replies.Text("done"));
        _app = new TestApp(model, settings: TestApp.DefaultSettings with { CheckCommand = Failing });

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.Conversations[1][^1].Content, Does.Contain($"Check `{Failing}` failed (exit code 1)"));
    }

    [Test]
    public async Task ARunnerGivesTheExitCodeAndOutputOfARealCommand()
    {
        var project = new TempProject();
        try
        {
            var runner = new CommandRunner(project.Location, TimeSpan.FromSeconds(30));

            var outcome = await runner.RunAsync(Failing, CancellationToken.None);

            Assert.That(outcome.ExitCode, Is.EqualTo(1));
            Assert.That(outcome.Output, Is.Not.Empty);
        }
        finally
        {
            project.Dispose();
        }
    }

    [Test]
    public async Task TheSystemPromptTellsTheModelWhereANewClassGoesAndWhatToCreateFirst()
    {
        var model = new ScriptedModel(Replies.Text("hi"));
        _app = new TestApp(model);

        await _app.Agent.RespondAsync("go", CancellationToken.None);

        Assert.That(model.LastConversation[0].Content,
            Does.Contain("Put each new class in its own file, named after it, and create it before you register it"));
    }
}
