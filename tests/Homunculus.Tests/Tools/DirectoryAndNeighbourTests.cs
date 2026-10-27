using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

/// <summary>Telling a model it used a directory as a file, and nudging it to read a neighbour before it writes a new file.</summary>
public class DirectoryAndNeighbourTests
{
    private TempProject _project = null!;
    private ReadFiles _reads = null!;
    private EditFileTool _edit = null!;
    private ReadFileTool _read = null!;
    private ListFilesTool _list = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _reads = new ReadFiles();
        _edit = new EditFileTool(_project.Location, reads: _reads);
        _read = new ReadFileTool(_project.Location, _reads);
        _list = new ListFilesTool(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Edit(string path, string oldText, string newText) =>
        _edit.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(
            new { path, old_str = oldText, new_str = newText })), CancellationToken.None);

    private Task<string> Read(string path) => _read.RunAsync(Json.Parse($$"""{"path":"{{path}}"}"""), CancellationToken.None);

    [TestCase("")]
    [TestCase("old text")]
    public void EditFileSaysWhenThePathIsADirectory(string oldText)
    {
        _project.Write("Commands/A.cs", "x");

        var error = Assert.ThrowsAsync<ToolException>(() => Edit("Commands", oldText, "new"));

        Assert.That(error!.Message, Is.EqualTo("Commands is a directory, not a file. Give the path of a file, for example Commands/<file name>."));
    }

    [Test]
    public void EditFileSaysItForTheProjectRootToo()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Edit(".", "", "new"));

        Assert.That(error!.Message, Does.StartWith(". is a directory, not a file"));
    }

    [Test]
    public void ReadFileSaysWhenThePathIsADirectory()
    {
        _project.Write("Commands/A.cs", "x");

        var error = Assert.ThrowsAsync<ToolException>(() => Read("Commands"));

        Assert.That(error!.Message, Is.EqualTo("Commands is a directory, not a file. Use list_files to see what is in it."));
    }

    [Test]
    public void ReadFileStillSaysNotFoundForAMissingPath()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Read("nope.txt"));

        Assert.That(error!.Message, Does.StartWith("File not found"));
    }

    [Test]
    public void ListFilesSaysWhenThePathIsAFile()
    {
        _project.Write("App.cs", "x");

        var error = Assert.ThrowsAsync<ToolException>(() => _list.RunAsync(Json.Parse("""{"path":"App.cs"}"""), CancellationToken.None));

        Assert.That(error!.Message, Is.EqualTo("App.cs is a file, not a directory. Use read_file to read it."));
    }

    [Test]
    public void ListFilesStillSaysNotADirectoryForAMissingPath()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => _list.RunAsync(Json.Parse("""{"path":"nope"}"""), CancellationToken.None));

        Assert.That(error!.Message, Does.StartWith("Not a directory"));
    }

    [Test]
    public async Task RemindsTheModelWhenItCreatesAFileNextToFilesItHasNotRead()
    {
        _project.Write("Commands/ICommand.cs", "interface");

        var result = await Edit("Commands/New.cs", "", "class New {}");

        Assert.That(result, Does.StartWith("Created").And.Contain("You have not read any other file in Commands yet")
            .And.Contain("read one and compare"));
    }

    [Test]
    public async Task DoesNotRemindOnceAFileInThatFolderWasRead()
    {
        _project.Write("Commands/ICommand.cs", "interface");
        await Read("Commands/ICommand.cs");

        var result = await Edit("Commands/New.cs", "", "class New {}");

        Assert.That(result, Does.Not.Contain("You have not read"));
    }

    [Test]
    public async Task ReadingAFileInAnotherFolderDoesNotCount()
    {
        _project.Write("Commands/ICommand.cs", "interface");
        _project.Write("Other/B.cs", "b");
        await Read("Other/B.cs");

        var result = await Edit("Commands/New.cs", "", "class New {}");

        Assert.That(result, Does.Contain("You have not read any other file in Commands"));
    }

    [Test]
    public async Task DoesNotRemindWhenTheFolderHasNoOtherFile()
    {
        var result = await Edit("Brand/New.cs", "", "class New {}");

        Assert.That(result, Does.Not.Contain("You have not read"));
    }

    [Test]
    public async Task DoesNotRemindWhenEditingAnExistingFile()
    {
        _project.Write("Commands/A.cs", "one");
        _project.Write("Commands/B.cs", "two");

        var result = await Edit("Commands/A.cs", "one", "1");

        Assert.That(result, Does.Not.Contain("You have not read"));
    }

    [Test]
    public async Task DoesNotRemindWhenReadsAreNotTracked()
    {
        _project.Write("Commands/ICommand.cs", "interface");

        var result = await new EditFileTool(_project.Location).RunAsync(
            Json.Parse("""{"path":"Commands/New.cs","old_str":"","new_str":"x"}"""), CancellationToken.None);

        Assert.That(result, Does.Not.Contain("You have not read"));
    }

    [Test]
    public void AFailedReadIsNotRecorded()
    {
        _project.Write("Commands/A.cs", "x");

        Assert.ThrowsAsync<ToolException>(() => Read("Commands/Missing.cs"));

        Assert.That(_reads.HasReadAnyIn(_project.PathOf("Commands")), Is.False);
    }

    [Test]
    public async Task ForgetsEverythingItHasRead()
    {
        _project.Write("Commands/A.cs", "x");
        await Read("Commands/A.cs");

        _reads.Forget();

        Assert.That(_reads.HasReadAnyIn(_project.PathOf("Commands")), Is.False);
    }
}
