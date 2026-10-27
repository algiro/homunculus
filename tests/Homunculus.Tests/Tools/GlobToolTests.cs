using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class GlobToolTests
{
    private TempProject _project = null!;
    private GlobTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new GlobTool(_project.Location);
        _project.Write("src/App.cs", "a");
        _project.Write("src/Commands/ICommand.cs", "b");
        _project.Write("src/Commands/ExitCommand.cs", "c");
        _project.Write("src/Homunculus.csproj", "d");
        _project.Write("tests/AppTests.cs", "e");
        _project.Write("README.md", "f");
        _project.Write("src/bin/Debug/Old.cs", "skipped");
        _project.Write("src/obj/Old.cs", "skipped");
        _project.Write(".git/hooks/Hook.cs", "skipped");
        _project.Write("node_modules/pkg/index.cs", "skipped");
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string json) => _tool.RunAsync(Json.Parse(json), CancellationToken.None);

    [Test]
    public async Task FindsFilesByNameInAnyFolder() =>
        Assert.That(await Run("""{"pattern":"*Command.cs"}"""), Is.EqualTo("src/Commands/ExitCommand.cs\nsrc/Commands/ICommand.cs"));

    [Test]
    public async Task ListsPathsRelativeToTheProjectWithForwardSlashes() =>
        Assert.That(await Run("""{"pattern":"src/**/*.cs"}"""), Is.EqualTo("src/App.cs\nsrc/Commands/ExitCommand.cs\nsrc/Commands/ICommand.cs"));

    [Test]
    public async Task SkipsBuildOutputVersionControlAndPackageFolders() =>
        Assert.That(await Run("""{"pattern":"**/*.cs"}"""), Does.Not.Contain("Old.cs").And.Not.Contain("Hook.cs").And.Not.Contain("index.cs"));

    [Test]
    public async Task SearchesOnlyUnderThePathButShowsPathsFromTheProjectRoot() =>
        Assert.That(await Run("""{"pattern":"*.cs","path":"src/Commands"}"""), Is.EqualTo("src/Commands/ExitCommand.cs\nsrc/Commands/ICommand.cs"));

    [Test]
    public async Task MatchesThePatternAgainstPathsInsideThePath() =>
        Assert.That(await Run("""{"pattern":"Commands/*.cs","path":"src"}"""), Is.EqualTo("src/Commands/ExitCommand.cs\nsrc/Commands/ICommand.cs"));

    [Test]
    public async Task SaysWhenNothingMatches() =>
        Assert.That(await Run("""{"pattern":"*.xyz"}"""), Is.EqualTo("No files match."));

    [Test]
    public async Task CutsALongListAndSaysSo()
    {
        for (var i = 0; i < Limits.SearchResults + 5; i++) _project.Write($"many/f{i:D4}.txt", "x");

        var result = await Run("""{"pattern":"many/*.txt"}""");

        Assert.That(result.Split('\n'), Has.Length.EqualTo(Limits.SearchResults + 1));
        Assert.That(result, Does.EndWith("[more files not shown; use a narrower pattern or path]"));
    }

    [Test]
    public async Task ShowsExactlyTheMaximumWithoutANotice()
    {
        for (var i = 0; i < Limits.SearchResults; i++) _project.Write($"exact/f{i:D4}.txt", "x");

        Assert.That(await Run("""{"pattern":"exact/*.txt"}"""), Does.Not.Contain("more files"));
    }

    [Test]
    public void FailsWhenThePatternIsMissing() => Assert.ThrowsAsync<ToolException>(() => Run("{}"));

    [Test]
    public void FailsForAFolderThatDoesNotExist() =>
        Assert.ThrowsAsync<ToolException>(() => Run("""{"pattern":"*.cs","path":"nope"}"""));

    [Test]
    public void SaysWhenThePathIsAFile()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("""{"pattern":"*.cs","path":"src/App.cs"}"""));

        Assert.That(error!.Message, Is.EqualTo("src/App.cs is a file, not a folder."));
    }

    [Test]
    public void ReadsTheSearchRootWithoutNeedingAnArgumentForIt() =>
        Assert.That(_tool.EffectOf(Json.Parse("""{"pattern":"*.cs"}""")), Is.EqualTo(new ToolEffect(ToolAccess.Read, _project.Location.Root)));

    [Test]
    public void ReadsTheGivenFolder() =>
        Assert.That(_tool.EffectOf(Json.Parse("""{"pattern":"*.cs","path":"src"}""")),
            Is.EqualTo(new ToolEffect(ToolAccess.Read, _project.Location.Resolve("src"))));

    [Test]
    public void ALookOutsideTheProjectIsRecognizedAsOutside() =>
        Assert.That(_project.Location.IsOutside(_tool.EffectOf(Json.Parse("""{"pattern":"*","path":"../.."}"""))), Is.True);
}
