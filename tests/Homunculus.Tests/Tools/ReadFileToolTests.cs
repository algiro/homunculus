using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ReadFileToolTests
{
    private TempProject _project = null!;
    private ReadFileTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new ReadFileTool(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    [Test]
    public async Task ReturnsTheFileContent()
    {
        _project.Write("a.txt", "hello");

        Assert.That(await _tool.RunAsync(Json.Parse("""{"path":"a.txt"}"""), CancellationToken.None), Is.EqualTo("hello"));
    }

    [Test]
    public async Task TruncatesLargeFiles()
    {
        _project.Write("big.txt", new string('x', Limits.OutputChars + 10));

        var result = await _tool.RunAsync(Json.Parse("""{"path":"big.txt"}"""), CancellationToken.None);

        Assert.That(result, Does.StartWith(new string('x', Limits.OutputChars)));
        Assert.That(result, Does.EndWith("[truncated at 100000 characters]"));
    }

    [Test]
    public void FailsForAMissingFile() =>
        Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(Json.Parse("""{"path":"nope.txt"}"""), CancellationToken.None));

    [Test]
    public void FailsWhenThePathArgumentIsMissing() =>
        Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(Json.Parse("{}"), CancellationToken.None));

    [Test]
    public void FailsWhenTheArgumentsAreNotAnObject() =>
        Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(Json.Parse("[]"), CancellationToken.None));

    [Test]
    public void ReadsTheGivenFile() =>
        Assert.That(_tool.EffectOf(Json.Parse("""{"path":"a.txt"}""")),
            Is.EqualTo(new ToolEffect(ToolAccess.Read, _project.Location.Resolve("a.txt"))));
}
