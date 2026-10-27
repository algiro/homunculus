using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

/// <summary>What edit_file tells a model that is about to get stuck.</summary>
public class EditFileHintTests
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
    public void RefusesNewFileTextWhoseLineBreaksAreTheCharactersBackslashN()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("A.cs", "", """using X;\n\nclass A {}"""));

        Assert.That(error!.Message, Does.StartWith("new_str has no line breaks").And.Contain("real line breaks"));
        Assert.That(_project.Exists("A.cs"), Is.False);
    }

    [Test]
    public async Task AcceptsOneLineThatMentionsABackslashNOnce()
    {
        await Run("A.cs", "", """Console.WriteLine("a\n");""");

        Assert.That(_project.Exists("A.cs"), Is.True);
    }

    [Test]
    public async Task AcceptsBackslashNInsideTextThatHasRealLineBreaks()
    {
        await Run("A.cs", "", "var a = \"x\\ny\\nz\";\nvar b = 1;\n");

        Assert.That(_project.Read("A.cs"), Does.Contain("""x\ny\nz"""));
    }

    [Test]
    public void RefusesAnEditWhoseMultiLineOldTextIsReplacedByEscapedText()
    {
        _project.Write("A.cs", "one\ntwo\n");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("A.cs", "one\ntwo", """1\n2\n3"""));

        Assert.That(error!.Message, Does.StartWith("new_str has no line breaks"));
        Assert.That(_project.Read("A.cs"), Is.EqualTo("one\ntwo\n"));
    }

    [Test]
    public void SaysSoWhenTheQuotedTextHasEscapedLineBreaksAndTheFileHasNone()
    {
        _project.Write("A.cs", "one\ntwo\n");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("A.cs", """one\ntwo\nthree""", "x"));

        Assert.That(error!.Message, Does.Contain("old_str was not found").And.Contain("old_str has no line breaks"));
    }

    [Test]
    public void ShowsTheClosestRealLineWhenTheQuotedTextIsNotThere()
    {
        _project.Write("App.cs", "commands\r\n    .Add(new HelpCommand(commands, ui))\r\n    .Add(new PolicyCommand(Policy, ui))\r\n");

        var error = Assert.ThrowsAsync<ToolException>(() =>
            Run("App.cs", "registry.Register<PolicyCommand>();", "registry.Register<VersionCommand>();"));

        Assert.That(error!.Message, Does.Contain("Line 1 of old_str is not in the file: registry.Register<PolicyCommand>();"));
        Assert.That(error.Message, Does.Contain("The closest line in the file is line 3:     .Add(new PolicyCommand(Policy, ui))"));
    }

    [Test]
    public void PointsAtTheWrongLineOfAMultiLineQuote()
    {
        _project.Write("App.cs", "commands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new ToolsCommand(Tools, ui))\n");

        var error = Assert.ThrowsAsync<ToolException>(() =>
            Run("App.cs", "commands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new ToolCommand(Tools, ui))", "x"));

        Assert.That(error!.Message, Does.Contain("Line 3 of old_str is not in the file:     .Add(new ToolCommand(Tools, ui))"));
        Assert.That(error.Message, Does.Contain("The closest line in the file is line 3:     .Add(new ToolsCommand(Tools, ui))"));
    }

    [Test]
    public void LeavesOutTheClosestLineWhenNothingIsSimilar()
    {
        _project.Write("App.cs", "alpha\nbeta\n");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("App.cs", "zzz_missing()", "x"));

        Assert.That(error!.Message, Does.Not.Contain("closest line"));
    }

    [Test]
    public void SuggestsDeletingAndRecreatingAFileThatCannotBeFixed()
    {
        _project.Write("A.cs", "x");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("A.cs", "y", "z"));

        Assert.That(error!.Message, Does.Contain("delete it and create it again"));
    }
}
