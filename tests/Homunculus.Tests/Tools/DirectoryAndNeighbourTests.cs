using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

/// <summary>Telling a model it used a directory as a file, and nudging it to read a neighbour before it writes a new file.</summary>
public class DirectoryAndNeighbourTests
{
    private TempProject _project = null!;
    private ReadFileTool _read = null!;
    private ListFilesTool _list = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _read = new ReadFileTool(_project.Location);
        _list = new ListFilesTool(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Read(string path) => _read.RunAsync(Json.Parse($$"""{"path":"{{path}}"}"""), CancellationToken.None);

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
}
