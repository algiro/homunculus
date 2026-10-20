using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ListFilesToolTests
{
    private TempProject _project = null!;
    private ListFilesTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new ListFilesTool(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    [Test]
    public async Task ListsEntriesSortedWithFoldersMarked()
    {
        _project.Write("b.txt", "");
        _project.Write("a.txt", "");
        _project.Write("src/code.cs", "");

        var result = await _tool.RunAsync(Json.Parse("{}"), CancellationToken.None);

        Assert.That(result.Split('\n'), Is.EqualTo(new[] { "a.txt", "b.txt", "src/" }));
    }

    [Test]
    public async Task ListsTheGivenSubfolder()
    {
        _project.Write("src/code.cs", "");

        var result = await _tool.RunAsync(Json.Parse("""{"path":"src"}"""), CancellationToken.None);

        Assert.That(result, Is.EqualTo("code.cs"));
    }

    [Test]
    public async Task LimitsTheNumberOfEntriesAndSaysHowManyAreHidden()
    {
        for (var i = 0; i < Limits.ListEntries + 5; i++) _project.Write($"f{i:D4}.txt", "");

        var lines = (await _tool.RunAsync(Json.Parse("{}"), CancellationToken.None)).Split('\n');

        Assert.That(lines, Has.Length.EqualTo(Limits.ListEntries + 1));
        Assert.That(lines[^1], Does.Contain("5 more entries"));
    }

    [Test]
    public void FailsForAMissingFolder() =>
        Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(Json.Parse("""{"path":"nope"}"""), CancellationToken.None));

    [Test]
    public void TargetsTheProjectRootByDefault() =>
        Assert.That(_tool.TargetPath(Json.Parse("{}")), Is.EqualTo(_project.Root));

    [Test]
    public void IsAReadOperation() => Assert.That(_tool.Access, Is.EqualTo(ToolAccess.Read));
}
