using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class InsertLinesToolTests
{
    private const string Chain = "commands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new ClearCommand(agent, ui))\n    .Add(new ExitCommand());\nRepl = new Repl(commands);\n";

    private TempProject _project = null!;
    private ChangedFiles _changes = null!;
    private InsertLinesTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _changes = new ChangedFiles(_project.Location);
        _tool = new InsertLinesTool(_project.Location, _changes);
        _project.Write("App.cs", Chain);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string line, string text, string? position = null, string path = "App.cs") =>
        _tool.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string?> { ["path"] = path, ["line"] = line, ["text"] = text, ["position"] = position })), CancellationToken.None);

    [Test]
    public async Task InsertsAfterTheAnchorLineByDefault()
    {
        await Run(".Add(new ClearCommand(agent, ui))", ".Add(new VersionCommand())");

        Assert.That(_project.Read("App.cs"), Is.EqualTo(
            "commands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new ClearCommand(agent, ui))\n    .Add(new VersionCommand())\n    .Add(new ExitCommand());\nRepl = new Repl(commands);\n"));
    }

    [Test]
    public async Task InsertsBeforeTheAnchorLineWhenAsked()
    {
        await Run(".Add(new ExitCommand());", ".Add(new VersionCommand())", "before");

        Assert.That(_project.Read("App.cs"), Does.Contain("    .Add(new VersionCommand())\n    .Add(new ExitCommand());"));
    }

    [TestCase("after")]
    [TestCase("AFTER")]
    [TestCase("")]
    public async Task AcceptsTheUsualSpellingsOfAfter(string position)
    {
        await Run("commands", "// note", position);

        Assert.That(_project.Read("App.cs"), Does.StartWith("commands\n// note\n"));
    }

    [Test]
    public void RefusesAnUnknownPosition()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("commands", "x", "beside"));

        Assert.That(error!.Message, Is.EqualTo("position must be 'after' or 'before'."));
    }

    [Test]
    public async Task GivesTheNewLinesTheIndentationOfTheAnchor()
    {
        await Run("    .Add(new HelpCommand(commands, ui))", "        .Add(new A())\n        .Add(new B())");

        Assert.That(_project.Read("App.cs"), Does.Contain("    .Add(new HelpCommand(commands, ui))\n    .Add(new A())\n    .Add(new B())\n"));
    }

    [Test]
    public async Task KeepsTheRelativeIndentationInsideTheInsertedText()
    {
        await Run("commands", "if (x)\n{\n    y();\n}");

        Assert.That(_project.Read("App.cs"), Does.StartWith("commands\nif (x)\n{\n    y();\n}\n"));
    }

    [Test]
    public async Task IgnoresTheIndentationOfTheAnchorTheModelWrote()
    {
        await Run("          .Add(new ExitCommand());", "// end");

        Assert.That(_project.Read("App.cs"), Does.Contain("    .Add(new ExitCommand());\n    // end\n"));
    }

    [Test]
    public async Task FallsBackToTheOneLineThatContainsTheAnchor()
    {
        await Run(".Add(new ExitCommand())", "// after exit");

        Assert.That(_project.Read("App.cs"), Does.Contain("    .Add(new ExitCommand());\n    // after exit\n"));
    }

    [Test]
    public async Task PrefersALineEqualToTheAnchorOverLinesThatOnlyContainIt()
    {
        _project.Write("B.cs", "a\nitem\nitem two\nitem three\n");

        await Run("item", "NEW", path: "B.cs");

        Assert.That(_project.Read("B.cs"), Is.EqualTo("a\nitem\nNEW\nitem two\nitem three\n"));
    }

    [Test]
    public void RefusesAnAnchorThatMatchesSeveralLinesAndNamesThem()
    {
        _project.Write("B.cs", "}\nx\n}\n}\n");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("}", "y", path: "B.cs"));

        Assert.That(error!.Message, Does.Contain("matches 3 lines").And.Contain("line numbers 1, 3, 4"));
        Assert.That(_project.Read("B.cs"), Is.EqualTo("}\nx\n}\n}\n"));
    }

    [Test]
    public void ShowsTheClosestLineWhenTheAnchorIsNotInTheFile()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("registry.Register<ExitCommand>();", "x"));

        Assert.That(error!.Message, Does.Contain("is not in the file").And.Contain("The closest line is line 4:     .Add(new ExitCommand());"));
    }

    [Test]
    public void LeavesOutTheClosestLineWhenNothingIsSimilar()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("zzz_missing()", "x"));

        Assert.That(error!.Message, Is.EqualTo("The line 'zzz_missing()' is not in the file."));
    }

    [Test]
    public async Task KeepsWindowsLineEndings()
    {
        _project.Write("W.cs", "one\r\ntwo\r\n");

        await Run("one", "1a\n1b", path: "W.cs");

        Assert.That(_project.Read("W.cs"), Is.EqualTo("one\r\n1a\r\n1b\r\ntwo\r\n"));
    }

    [Test]
    public async Task KeepsAFileWithoutAFinalNewlineWithout()
    {
        _project.Write("N.cs", "one\ntwo");

        await Run("two", "three", path: "N.cs");

        Assert.That(_project.Read("N.cs"), Is.EqualTo("one\ntwo\nthree"));
    }

    [Test]
    public async Task IgnoresOneTrailingLineBreakInTheText()
    {
        await Run("commands", "// note\n");

        Assert.That(_project.Read("App.cs"), Does.StartWith("commands\n// note\n    .Add"));
    }

    [Test]
    public async Task ShowsTheInsertedLinesInContext()
    {
        var result = await Run(".Add(new ClearCommand(agent, ui))", ".Add(new VersionCommand())");

        Assert.That(result, Is.EqualTo(
            $"Inserted 1 line after line 3 of App.cs:\n" +
            "     2:     .Add(new HelpCommand(commands, ui))\n" +
            "     3:     .Add(new ClearCommand(agent, ui))\n" +
            "+    4:     .Add(new VersionCommand())\n" +
            "     5:     .Add(new ExitCommand());\n" +
            "     6: Repl = new Repl(commands);"));
    }

    [Test]
    public async Task NamesTheAnchorsNewLineNumberWhenInsertingBefore()
    {
        var result = await Run("commands", "a\nb", "before");

        Assert.That(result, Does.StartWith("Inserted 2 lines before line 3 of App.cs:"));
    }

    [Test]
    public async Task RecordsTheFileAsEdited()
    {
        await Run("commands", "// note");

        Assert.That(_changes.Summary(), Does.EndWith("edited App.cs"));
    }

    [Test]
    public void ChangesNothingWhenItFails()
    {
        Assert.ThrowsAsync<ToolException>(() => Run("zzz_missing()", "x"));

        Assert.That(_project.Read("App.cs"), Is.EqualTo(Chain));
        Assert.That(_changes.Summary(), Is.Null);
    }

    [Test]
    public void RefusesEscapedLineBreaksInTheText()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("commands", """a\nb\nc"""));

        Assert.That(error!.Message, Does.StartWith("text has no line breaks"));
        Assert.That(_project.Read("App.cs"), Is.EqualTo(Chain));
    }

    [Test]
    public void SaysWhenThePathIsADirectory()
    {
        _project.Write("Commands/A.cs", "x");

        var error = Assert.ThrowsAsync<ToolException>(() => Run("x", "y", path: "Commands"));

        Assert.That(error!.Message, Does.StartWith("Commands is a directory, not a file."));
    }

    [Test]
    public void SaysHowToCreateAFileThatDoesNotExist()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("x", "y", path: "Nope.cs"));

        Assert.That(error!.Message, Does.StartWith("File not found").And.Contain("use write_file"));
    }

    [TestCase("", "x")]
    [TestCase("   ", "x")]
    [TestCase("a\nb", "x")]
    [TestCase("commands", "  ")]
    public void RefusesAnEmptyOrMultiLineAnchorAndEmptyText(string line, string text) =>
        Assert.ThrowsAsync<ToolException>(() => Run(line, text));

    [Test]
    public void FailsWhenAnArgumentIsMissing() =>
        Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(Json.Parse("""{"path":"App.cs","line":"commands"}"""), CancellationToken.None));

    [Test]
    public void WritesTheGivenFile() =>
        Assert.That(_tool.EffectOf(Json.Parse("""{"path":"App.cs","line":"x","text":"y"}""")),
            Is.EqualTo(new ToolEffect(ToolAccess.Write, _project.Location.Resolve("App.cs"))));
}
