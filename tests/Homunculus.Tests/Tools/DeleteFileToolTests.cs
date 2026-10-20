using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class DeleteFileToolTests
{
    private TempProject _project = null!;
    private DeleteFileTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new DeleteFileTool(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string path) =>
        _tool.RunAsync(Json.Parse($$"""{"path":"{{path}}"}"""), CancellationToken.None);

    [Test]
    public async Task DeletesAFile()
    {
        _project.Write("a.txt", "x");

        var result = await Run("a.txt");

        Assert.That(_project.Exists("a.txt"), Is.False);
        Assert.That(result, Does.StartWith("Deleted"));
    }

    [Test]
    public void RefusesToDeleteAFolder()
    {
        _project.Write("folder/a.txt", "x");

        Assert.ThrowsAsync<ToolException>(() => Run("folder"));
        Assert.That(_project.Exists("folder/a.txt"), Is.True);
    }

    [Test]
    public void FailsForAMissingFile() => Assert.ThrowsAsync<ToolException>(() => Run("nope.txt"));

    [Test]
    public void WritesTheGivenFile() =>
        Assert.That(_tool.EffectOf(Json.Parse("""{"path":"a.txt"}""")),
            Is.EqualTo(new ToolEffect(ToolAccess.Write, _project.Location.Resolve("a.txt"))));
}
