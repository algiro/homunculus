using Homunculus.Llm;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

/// <summary>The check command a session found or was given, announced when the program starts.</summary>
public class CheckStartupTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private static CheckDetector EverythingInstalled => new(_ => true, false, "TEMP");

    private async Task<IReadOnlyList<string>> Start(string? setting, params string[] projectFiles)
    {
        // The project has to hold its files before the app looks, so the app is built from a pre-filled folder.
        var settings = TestApp.DefaultSettings with { CheckCommand = setting };
        _app = new TestApp(new ScriptedModel(), settings: settings, checks: EverythingInstalled, projectFiles: projectFiles);
        _app.Ui.TypesLines("/exit");

        await _app.Repl.RunAsync();
        return _app.Ui.Output;
    }

    [Test]
    public async Task TellsTheUserWhichCheckWasFoundForTheProject()
    {
        var output = await Start(null, "go.mod");

        Assert.That(_app.Ui.Banners, Has.Count.EqualTo(1));
        Assert.That(output[0], Does.StartWith("check: go build ./... (found for Go;").And.Contain("HOMUNCULUS_CHECK_COMMAND"));
    }

    [Test]
    public async Task SaysNothingExtraWhenNoCheckIsRecognised()
    {
        var output = await Start(null);

        Assert.That(output, Is.Empty);
    }

    [Test]
    public async Task SaysNothingExtraWhenTheCheckIsTurnedOff()
    {
        var output = await Start("off", "go.mod");

        Assert.That(output, Is.Empty);
    }

    [Test]
    public async Task NamesAnExplicitCommandToo()
    {
        var output = await Start("dotnet --version");

        Assert.That(output[0], Is.EqualTo("check: dotnet --version"));
    }

    [Test]
    public async Task RunsTheFoundCheckAfterAChange()
    {
        // "dotnet --version" stands in for what was found: the point is that a found command is the one that runs.
        await Start("dotnet --version");
        var runner = _app.Runner;

        var result = await runner.RunAsync(
            new ToolCall("c1", "write_file", Json.Parse("""{"path":"New.cs","content":"x"}""")), CancellationToken.None);

        Assert.That(result, Does.EndWith("Check passed."));
    }
}
