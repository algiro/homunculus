using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

/// <summary>The check after every change, and the undoing of an edit that breaks a passing build.</summary>
public class ChangeGuardTests
{
    private const string Broken = "A.cs(1,1): error X: broken";

    private TempProject _project = null!;
    private ChangedFiles _changes = null!;
    private FakeUserInterface _ui = null!;
    private FakeCommandRunner _commands = null!;
    private ToolRunner _runner = null!;

    [SetUp]
    public void SetUp() => Start(revert: true);

    [TearDown]
    public void TearDown()
    {
        _project?.Dispose();
        _project = null!;
    }

    // The build "fails" while any .cs file in the project contains the word BROKEN.
    private void Start(bool revert)
    {
        _project?.Dispose();
        _project = new TempProject();
        _project.Write("A.cs", "good");
        _changes = new ChangedFiles(_project.Location);
        _ui = new FakeUserInterface();
        _commands = new FakeCommandRunner { Respond = BuildResult };
        var tools = new ToolRegistry([
            new EditFileTool(_project.Location, _changes),
            new InsertLinesTool(_project.Location, _changes),
            new DeleteFileTool(_project.Location, _changes),
        ]);
        var approvals = new Approvals(new FixedPolicy(Verdict.Allow), _ui, _project.Location, _changes);
        var guard = new ChangeGuard(new ProjectCheck("build", _commands, _project.Location), _changes, _project.Location, _ui, revertBrokenEdits: revert);
        _runner = new ToolRunner(tools, approvals, _ui, null, guard);
    }

    private CommandOutcome BuildResult() =>
        Directory.EnumerateFiles(_project.Root, "*.cs", SearchOption.AllDirectories).Any(file => File.ReadAllText(file).Contains("BROKEN"))
            ? new CommandOutcome(Broken, 1)
            : new CommandOutcome("", 0);

    private Task<string> Run(string tool, string json) =>
        _runner.RunAsync(new ToolCall("c1", tool, Json.Parse(json)), CancellationToken.None);

    private const string EditToBroken = """{"path":"A.cs","old_str":"good","new_str":"BROKEN"}""";

    [Test]
    public async Task UndoesAnEditThatBreaksAPassingBuild()
    {
        var result = await Run("edit_file", EditToBroken);

        Assert.That(_project.Read("A.cs"), Is.EqualTo("good"));
        Assert.That(result, Does.StartWith("Error: Not applied: this change broke the build, which passed before, so A.cs is back as it was.")
            .And.Contain(Broken).And.Contain("create a class before you register it"));
        Assert.That(_commands.Commands, Has.Count.EqualTo(2), "once to see it fail, once to see it recover");
        Assert.That(_ui.Output, Does.Contain("  check: failed").And.Contain("  change undone"));
    }

    [Test]
    public async Task UndoesAnInsertionThatBreaksAPassingBuild()
    {
        var result = await Run("insert_lines", """{"path":"A.cs","line":"good","text":"BROKEN"}""");

        Assert.That(_project.Read("A.cs"), Is.EqualTo("good"));
        Assert.That(result, Does.StartWith("Error: Not applied"));
    }

    [Test]
    public async Task KeepsAnEditThatLeavesTheBuildPassing()
    {
        var result = await Run("edit_file", """{"path":"A.cs","old_str":"good","new_str":"better"}""");

        Assert.That(_project.Read("A.cs"), Is.EqualTo("better"));
        Assert.That(result, Does.StartWith("Edited").And.EndWith("\n\nCheck passed."));
        Assert.That(_commands.Commands, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task KeepsAnEditWhenTheBuildWasAlreadyBroken()
    {
        _project.Write("B.cs", "BROKEN");

        var result = await Run("edit_file", """{"path":"A.cs","old_str":"good","new_str":"better"}""");

        Assert.That(_project.Read("A.cs"), Is.EqualTo("better"), "the edit is put back after the build still fails without it");
        Assert.That(result, Does.StartWith("Edited").And.Contain("Check `build` failed"));
        Assert.That(_ui.Output, Does.Not.Contain("  change undone"));
    }

    [Test]
    public async Task LeavesTheAgentsOwnNewFilesAlone()
    {
        await Run("edit_file", """{"path":"C.cs","old_str":"","new_str":"fine"}""");

        var result = await Run("edit_file", """{"path":"C.cs","old_str":"fine","new_str":"BROKEN"}""");

        Assert.That(_project.Read("C.cs"), Is.EqualTo("BROKEN"));
        Assert.That(result, Does.StartWith("Edited").And.Contain("Check `build` failed"));
    }

    [Test]
    public async Task DoesNotUndoANewFileThatDoesNotBuild()
    {
        var result = await Run("edit_file", """{"path":"D.cs","old_str":"","new_str":"BROKEN"}""");

        Assert.That(_project.Exists("D.cs"), Is.True);
        Assert.That(result, Does.StartWith("Created").And.Contain("Check `build` failed"));
    }

    [Test]
    public async Task DoesNotBringBackAFileTheUserLetItDelete()
    {
        _ui.Answers('y');
        _commands.Respond = () => new CommandOutcome(Broken, 1);

        var result = await Run("delete_file", """{"path":"A.cs"}""");

        Assert.That(_project.Exists("A.cs"), Is.False);
        Assert.That(result, Does.StartWith("Deleted").And.Contain("Check `build` failed"));
    }

    [Test]
    public async Task OnlyReportsWhenUndoingIsTurnedOff()
    {
        Start(revert: false);

        var result = await Run("edit_file", EditToBroken);

        Assert.That(_project.Read("A.cs"), Is.EqualTo("BROKEN"));
        Assert.That(result, Does.StartWith("Edited").And.Contain("Check `build` failed"));
        Assert.That(_commands.Commands, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CountsAnUndoneChangeAsAFailureOfTheCall()
    {
        await Run("edit_file", EditToBroken);

        var second = await Run("edit_file", EditToBroken);
        await Run("edit_file", EditToBroken);

        Assert.That(second, Does.Contain("You already made this exact call"));
        Assert.That(_runner.IsStuckOn(new ToolCall("c1", "edit_file", Json.Parse(EditToBroken))), Is.True);
    }

    [Test]
    public async Task ShowsTheUndoneChangeToTheUserAsAnError()
    {
        await Run("edit_file", EditToBroken);

        Assert.That(_ui.Output, Has.Some.StartWith("  <- Error: Not applied"));
    }

    [Test]
    public async Task LeavesNoTraceOfTheUndoneChangeInTheSummary()
    {
        await Run("edit_file", EditToBroken);

        Assert.That(_changes.Summary(), Is.Null);
    }

    [Test]
    public async Task KeepsAnEarlierGoodEditOfTheSameFileInTheSummary()
    {
        await Run("edit_file", """{"path":"A.cs","old_str":"good","new_str":"better"}""");

        await Run("edit_file", """{"path":"A.cs","old_str":"better","new_str":"BROKEN"}""");

        Assert.That(_project.Read("A.cs"), Is.EqualTo("better"));
        Assert.That(_changes.Summary(), Does.EndWith("edited A.cs"));
    }

    [Test]
    public async Task NamesTheFileRelativeToTheProject()
    {
        _project.Write("sub/Inner.cs", "good");

        var result = await Run("edit_file", """{"path":"sub/Inner.cs","old_str":"good","new_str":"BROKEN"}""");

        Assert.That(result, Does.Contain("so sub/Inner.cs is back as it was"));
    }

    [Test]
    public void PutsTheEditBackWhenTheRecheckIsInterrupted()
    {
        var calls = 0;
        _commands.Respond = () => ++calls == 1 ? new CommandOutcome(Broken, 1) : throw new OperationCanceledException();

        Assert.CatchAsync<OperationCanceledException>(() => Run("edit_file", EditToBroken));

        Assert.That(_project.Read("A.cs"), Is.EqualTo("BROKEN"), "an interrupted recheck must not leave the file in a state nobody chose");
    }

    [Test]
    public async Task DoesNotCheckAfterThingsThatChangeNothing()
    {
        await Run("edit_file", """{"path":"A.cs","old_str":"zzz","new_str":"x"}""");

        Assert.That(_commands.Commands, Is.Empty);
    }
}
