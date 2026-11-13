using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class GrepToolTests
{
    private TempProject _project = null!;
    private GrepTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new GrepTool(_project.Location);
        _project.Write("src/App.cs", "using X;\n    .Add(new ExitCommand());\n    .Add(new HelpCommand());\n");
        _project.Write("src/Commands/ExitCommand.cs", "public sealed class ExitCommand : ICommand\n{\n}\n");
        _project.Write("docs/notes.md", "The ExitCommand quits.\n");
        _project.Write("src/bin/Old.cs", "ExitCommand in build output\n");
        _project.Write(".git/config", "ExitCommand in git\n");
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string json) => _tool.RunAsync(Json.Parse(json), CancellationToken.None);

    [Test]
    public async Task ListsMatchesAsFileLineText() =>
        Assert.That(await Run("""{"pattern":"ExitCommand"}"""), Is.EqualTo(
            "docs/notes.md:1: The ExitCommand quits.\n" +
            "src/App.cs:2: .Add(new ExitCommand());\n" +
            "src/Commands/ExitCommand.cs:1: public sealed class ExitCommand : ICommand"));

    [Test]
    public async Task UnderstandsRegularExpressions() =>
        Assert.That(await Run("""{"pattern":"class \\w+Command"}"""), Is.EqualTo("src/Commands/ExitCommand.cs:1: public sealed class ExitCommand : ICommand"));

    [Test]
    public async Task IsCaseSensitiveUnlessAskedOtherwise()
    {
        Assert.That(await Run("""{"pattern":"exitcommand"}"""), Is.EqualTo("No matches."));
        Assert.That(await Run("""{"pattern":"(?i)exitcommand"}"""), Does.Contain("src/App.cs:2"));
    }

    [Test]
    public async Task LimitsTheSearchToFilesMatchingInclude() =>
        Assert.That(await Run("""{"pattern":"ExitCommand","include":"*.md"}"""), Is.EqualTo("docs/notes.md:1: The ExitCommand quits."));

    [Test]
    public async Task SearchesOnlyUnderThePath() =>
        Assert.That(await Run("""{"pattern":"ExitCommand","path":"src/Commands"}"""), Is.EqualTo("src/Commands/ExitCommand.cs:1: public sealed class ExitCommand : ICommand"));

    [Test]
    public async Task SearchesASingleFile() =>
        Assert.That(await Run("""{"pattern":"Command","path":"src/App.cs"}"""), Is.EqualTo(
            "src/App.cs:2: .Add(new ExitCommand());\nsrc/App.cs:3: .Add(new HelpCommand());"));

    [Test]
    public async Task AppliesIncludeToASingleFileToo() =>
        Assert.That(await Run("""{"pattern":"Command","path":"src/App.cs","include":"*.md"}"""), Is.EqualTo("No matches."));

    [Test]
    public async Task SkipsBuildOutputAndVersionControl() =>
        Assert.That(await Run("""{"pattern":"ExitCommand"}"""), Does.Not.Contain("build output").And.Not.Contain("in git"));

    [Test]
    public async Task SkipsLogsAndTranscripts()
    {
        _project.Write("logs/run.txt", "ExitCommand in a logs folder");
        _project.Write("debug.jsonl", "ExitCommand in a json log");
        _project.Write("docs/tests/run.log", "ExitCommand in a transcript");

        var result = await Run("""{"pattern":"ExitCommand"}""");

        Assert.That(result, Does.Not.Contain("logs folder").And.Not.Contain("json log").And.Not.Contain("transcript"));
        Assert.That(result, Does.Contain("src/App.cs:2"));
    }

    [Test]
    public async Task SearchesALogWhenItIsNamedAsThePath()
    {
        _project.Write("run.log", "hit\n");

        Assert.That(await Run("""{"pattern":"hit","path":"run.log"}"""), Is.EqualTo("run.log:1: hit"));
    }

    [Test]
    public async Task SkipsBinaryFiles()
    {
        File.WriteAllBytes(_project.PathOf("data.bin"), [65, 66, 0, 67, 68]);

        Assert.That(await Run("""{"pattern":"AB"}"""), Is.EqualTo("No matches."));
    }

    [Test]
    public async Task SkipsFilesThatAreTooBig()
    {
        _project.Write("big.txt", new string('x', 1_000_001) + "\nneedle");

        Assert.That(await Run("""{"pattern":"needle"}"""), Is.EqualTo("No matches."));
    }

    [Test]
    public async Task TrimsTheTextAndCutsAVeryLongLine()
    {
        _project.Write("long.txt", "   " + new string('y', 500));

        var result = await Run("""{"pattern":"yyy","include":"long.txt"}""");

        Assert.That(result, Does.StartWith("long.txt:1: yyy").And.EndWith("..."));
        Assert.That(result, Has.Length.EqualTo("long.txt:1: ".Length + 203));
    }

    [Test]
    public async Task SaysWhenNothingMatches() =>
        Assert.That(await Run("""{"pattern":"zzz_not_there"}"""), Is.EqualTo("No matches."));

    [Test]
    public async Task CutsALongResultListAndSaysSo()
    {
        _project.Write("many.txt", string.Concat(Enumerable.Repeat("hit\n", Limits.SearchResults + 5)));

        var result = await Run("""{"pattern":"hit","include":"many.txt"}""");

        Assert.That(result.Split('\n'), Has.Length.EqualTo(Limits.SearchResults + 1));
        Assert.That(result, Does.EndWith("[more matches not shown; use a narrower pattern, path or include]"));
    }

    [Test]
    public async Task ShowsExactlyTheMaximumWithoutANotice()
    {
        _project.Write("exact.txt", string.Concat(Enumerable.Repeat("hit\n", Limits.SearchResults)));

        Assert.That(await Run("""{"pattern":"hit","include":"exact.txt"}"""), Does.Not.Contain("more matches"));
    }

    [Test]
    public void RefusesAnInvalidRegularExpression()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("""{"pattern":"(unclosed"}"""));

        Assert.That(error!.Message, Does.StartWith("Invalid regular expression"));
    }

    [Test]
    public void FailsWhenThePatternIsMissing() => Assert.ThrowsAsync<ToolException>(() => Run("{}"));

    [Test]
    public void FailsForAPathThatDoesNotExist() =>
        Assert.ThrowsAsync<ToolException>(() => Run("""{"pattern":"x","path":"nope"}"""));

    [Test]
    public void ReadsTheSearchRoot()
    {
        Assert.That(_tool.EffectOf(Json.Parse("""{"pattern":"x"}""")), Is.EqualTo(new ToolEffect(ToolAccess.Read, _project.Location.Root)));
        Assert.That(_project.Location.IsOutside(_tool.EffectOf(Json.Parse("""{"pattern":"x","path":"../.."}"""))), Is.True);
    }
}
