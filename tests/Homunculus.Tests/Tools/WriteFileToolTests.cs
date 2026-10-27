using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class WriteFileToolTests
{
    private TempProject _project = null!;
    private ChangedFiles _changes = null!;
    private ReadFiles _reads = null!;
    private WriteFileTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _changes = new ChangedFiles(_project.Location);
        _reads = new ReadFiles();
        _tool = new WriteFileTool(_project.Location, _changes, _reads);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string path, string content) =>
        _tool.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(new { path, content })), CancellationToken.None);

    [Test]
    public async Task CreatesTheFileWithExactlyTheGivenContent()
    {
        var result = await Run("New.cs", "line one\nline two\n");

        Assert.That(_project.Read("New.cs"), Is.EqualTo("line one\nline two\n"));
        Assert.That(result, Does.StartWith("Created"));
    }

    [Test]
    public async Task CreatesMissingFolders()
    {
        await Run("a/b/New.cs", "x");

        Assert.That(_project.Read("a/b/New.cs"), Is.EqualTo("x"));
    }

    [Test]
    public async Task CreatesAnEmptyFile()
    {
        await Run("Empty.txt", "");

        Assert.That(_project.Read("Empty.txt"), Is.Empty);
    }

    [Test]
    public void RefusesAFileThatAlreadyExistsAndPointsToTheEditingTools()
    {
        _project.Write("Old.cs", "keep me");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("Old.cs", "overwrite"));

        Assert.That(error!.Message, Does.StartWith("File already exists").And.EndWith("To change it, use edit_file or insert_lines."));
        Assert.That(_project.Read("Old.cs"), Is.EqualTo("keep me"));
    }

    [Test]
    public void SaysWhenThePathIsADirectory()
    {
        _project.Write("Commands/A.cs", "x");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("Commands", "x"));

        Assert.That(error!.Message, Does.StartWith("Commands is a directory, not a file."));
    }

    [Test]
    public void RefusesContentWhoseLineBreaksAreTheCharactersBackslashN()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("New.cs", """a\nb\nc"""));

        Assert.That(error!.Message, Does.StartWith("content has no line breaks"));
        Assert.That(_project.Exists("New.cs"), Is.False);
    }

    [Test]
    public async Task RecordsTheFileAsCreated()
    {
        await Run("New.cs", "x");

        Assert.That(_changes.Summary(), Does.EndWith("created New.cs"));
        Assert.That(_changes.WasCreatedInSession(_project.Location.Resolve("New.cs")), Is.True);
    }

    [Test]
    public async Task RemindsTheModelOfTheUnreadFilesTheNewOneBuildsOn()
    {
        _project.Write("Commands/ICommand.cs", "interface ICommand {}");

        var result = await Run("Commands/New.cs", "class New : ICommand {}");

        Assert.That(result, Does.Contain("It uses ICommand, defined in Commands/ICommand.cs, which you have not read"));
    }

    [Test]
    public void FailsWhenAnArgumentIsMissing() =>
        Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(Json.Parse("""{"path":"New.cs"}"""), CancellationToken.None));

    [Test]
    public void WritesTheGivenFile() =>
        Assert.That(_tool.EffectOf(Json.Parse("""{"path":"New.cs","content":"x"}""")),
            Is.EqualTo(new ToolEffect(ToolAccess.Write, _project.Location.Resolve("New.cs"))));

    [Test]
    public void IsDescribedAsCreateOnly() =>
        Assert.That(_tool.Description, Does.Contain("new file").And.Contain("refuses a file that already exists"));

    [Test]
    public void EditFileAndRunCommandPointToIt()
    {
        var edit = new EditFileTool(_project.Location);
        var run = new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1));

        Assert.That(edit.Description, Does.Contain("write_file is simpler"));
        Assert.That(run.Description, Does.Contain("Do not use it to create or edit files").And.Contain("write_file"));
    }
}
