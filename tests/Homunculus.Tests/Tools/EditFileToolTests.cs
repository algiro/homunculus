using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class EditFileToolTests
{
    private TempProject _project = null!;
    private EditFileTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new EditFileTool(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string path, string oldText, string newText) =>
        _tool.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(
            new { path, old_str = oldText, new_str = newText })), CancellationToken.None);

    [Test]
    public async Task CreatesAFileWhenOldTextIsEmpty()
    {
        var result = await Run("new.txt", "", "content");

        Assert.That(_project.Read("new.txt"), Is.EqualTo("content"));
        Assert.That(result, Does.StartWith("Created"));
    }

    [Test]
    public async Task CreatesMissingFolders()
    {
        await Run("a/b/new.txt", "", "content");

        Assert.That(_project.Read("a/b/new.txt"), Is.EqualTo("content"));
    }

    [Test]
    public void RefusesToOverwriteAnExistingFileWhenCreating()
    {
        _project.Write("a.txt", "keep");

        Assert.ThrowsAsync<ToolException>(() => Run("a.txt", "", "replace"));
        Assert.That(_project.Read("a.txt"), Is.EqualTo("keep"));
    }

    [Test]
    public async Task ReplacesTheSingleMatch()
    {
        _project.Write("a.txt", "one two three");

        var result = await Run("a.txt", "two", "2");

        Assert.That(_project.Read("a.txt"), Is.EqualTo("one 2 three"));
        Assert.That(result, Does.StartWith("Edited"));
    }

    [Test]
    public void FailsWhenTheTextIsNotFound()
    {
        _project.Write("a.txt", "one");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("a.txt", "zzz", "x"));

        Assert.That(error!.Message, Does.Contain("not found"));
    }

    [Test]
    public void RefusesAnAmbiguousMatchAndLeavesTheFileUntouched()
    {
        _project.Write("a.txt", "x and x");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("a.txt", "x", "y"));

        Assert.That(error!.Message, Does.Contain("2 places"));
        Assert.That(_project.Read("a.txt"), Is.EqualTo("x and x"));
    }

    [Test]
    public void CountsOverlappingTextAsOneOccurrenceEachSoOnlyDistinctMatchesAreAmbiguous()
    {
        _project.Write("a.txt", "aaa");

        // "aa" occurs once when scanning without overlap past the first hit, then "a" remains.
        Assert.DoesNotThrowAsync(() => Run("a.txt", "aa", "b"));
    }

    [Test]
    public void FailsForAMissingFileWhenEditing() =>
        Assert.ThrowsAsync<ToolException>(() => Run("nope.txt", "a", "b"));

    [Test]
    public void FailsWhenAnArgumentIsMissing() =>
        Assert.ThrowsAsync<ToolException>(() =>
            _tool.RunAsync(Json.Parse("""{"path":"a.txt","old_str":""}"""), CancellationToken.None));

    [Test]
    public void IsAWriteOperation() => Assert.That(_tool.Access, Is.EqualTo(ToolAccess.Write));
}
