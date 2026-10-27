using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

/// <summary>What the file tools report back about the changes they make.</summary>
public class EditFileSafetyTests
{
    private TempProject _project = null!;
    private ChangedFiles _changes = null!;
    private EditFileTool _edit = null!;
    private DeleteFileTool _delete = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _changes = new ChangedFiles(_project.Location);
        _edit = new EditFileTool(_project.Location, _changes);
        _delete = new DeleteFileTool(_project.Location, _changes);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Edit(string path, string oldText, string newText) =>
        _edit.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(
            new { path, old_str = oldText, new_str = newText })), CancellationToken.None);

    [Test]
    public async Task WarnsWhenAnEditRemovesANameFromTheFile()
    {
        _project.Write("App.cs", "a\n.Add(new ExitCommand());\nb\n");

        var result = await Edit("App.cs", ".Add(new ExitCommand());", ".Add(new VersionCommand());");

        Assert.That(result, Does.StartWith("Edited").And.Contain("Warning: ExitCommand no longer appears in this file")
            .And.Contain("keep the old text in new_str"));
    }

    [Test]
    public async Task WarnsAboutSeveralNamesInOrder()
    {
        _project.Write("A.cs", "int zeta = beta;");

        var result = await Edit("A.cs", "int zeta = beta;", "var x = 1;");

        Assert.That(result, Does.Contain("Warning: beta, int, zeta no longer appear in this file"));
    }

    [Test]
    public async Task DoesNotWarnWhenTheOldNamesAreKept()
    {
        _project.Write("App.cs", ".Add(new ExitCommand());");

        var result = await Edit("App.cs", ".Add(new ExitCommand());", ".Add(new ExitCommand());\n.Add(new VersionCommand());");

        Assert.That(result, Does.Not.Contain("Warning"));
    }

    [Test]
    public async Task DoesNotWarnWhenTheNameStillAppearsElsewhereInTheFile()
    {
        _project.Write("App.cs", "ExitCommand one\nExitCommand two\n");

        var result = await Edit("App.cs", "ExitCommand one", "VersionCommand one");

        Assert.That(result, Does.Not.Contain("Warning"));
    }

    [Test]
    public void TheDescriptionTellsTheModelToKeepTheOldTextToAdd() =>
        Assert.That(_edit.Description, Does.Contain("to add something, keep the old text in new_str"));

    [Test]
    public async Task RecordsFilesItCreatesAndEdits()
    {
        await Edit("New.cs", "", "class A {}");
        _project.Write("Old.cs", "one");
        await Edit("Old.cs", "one", "two");

        Assert.That(_changes.Summary(), Does.Contain("created New.cs").And.Contain("edited Old.cs"));
        Assert.That(_changes.WasCreatedInSession(_project.PathOf("New.cs")), Is.True);
        Assert.That(_changes.WasCreatedInSession(_project.PathOf("Old.cs")), Is.False);
    }

    [Test]
    public async Task RecordsFilesItDeletes()
    {
        _project.Write("Old.cs", "x");

        await _delete.RunAsync(Json.Parse("""{"path":"Old.cs"}"""), CancellationToken.None);

        Assert.That(_changes.Summary(), Does.EndWith("deleted Old.cs"));
    }

    [Test]
    public void RecordsNothingWhenAnEditFails()
    {
        _project.Write("Old.cs", "one");

        Assert.ThrowsAsync<ToolException>(() => Edit("Old.cs", "zzz", "two"));

        Assert.That(_changes.Summary(), Is.Null);
    }

    [Test]
    public void TellsTheModelThatDeletingNeedsTheUsersOk() =>
        Assert.That(_delete.Description, Does.Contain("asked first").And.Contain("unless you created the file"));

    [Test]
    public void SaysWhenEveryQuotedLineExistsButNotAsOneBlock()
    {
        _project.Write("A.cs", "one\n\ntwo\nthree\n");

        var error = Assert.ThrowsAsync<ToolException>(() => Edit("A.cs", "one\ntwo", "x"));

        Assert.That(error!.Message, Does.Contain("Every line of old_str is in the file, but not together as one block")
            .And.Contain("Use just one of those lines"));
    }
}
