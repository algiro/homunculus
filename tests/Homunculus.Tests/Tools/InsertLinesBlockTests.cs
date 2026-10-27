using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

/// <summary>insert_lines with several consecutive lines as the anchor.</summary>
public class InsertLinesBlockTests
{
    private const string Source =
        "class A\n{\n    void One()\n    {\n        Do();\n    }\n\n    void Two()\n    {\n        Do();\n    }\n}\n";

    private TempProject _project = null!;
    private InsertLinesTool _tool = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _tool = new InsertLinesTool(_project.Location);
        _project.Write("A.cs", Source);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string> Run(string line, string text, string? position = null) =>
        _tool.RunAsync(Json.Parse(System.Text.Json.JsonSerializer.Serialize(
            new Dictionary<string, string?> { ["path"] = "A.cs", ["line"] = line, ["text"] = text, ["position"] = position })), CancellationToken.None);

    [Test]
    public async Task InsertsAfterTheLastLineOfTheBlock()
    {
        await Run("void One()\n{\n    Do();\n}", "void Between() { }");

        Assert.That(_project.Read("A.cs"), Does.Contain("    void One()\n    {\n        Do();\n    }\n    void Between() { }\n\n    void Two()"));
    }

    [Test]
    public async Task InsertsBeforeTheFirstLineOfTheBlock()
    {
        await Run("void Two()\n{", "[Obsolete]", "before");

        Assert.That(_project.Read("A.cs"), Does.Contain("\n\n    [Obsolete]\n    void Two()\n    {\n"));
    }

    [Test]
    public async Task IgnoresTheIndentationOfEachQuotedLine()
    {
        await Run("        void Two()\n  {\n                Do();", "// after the call");

        Assert.That(_project.Read("A.cs"), Does.Contain("    void Two()\n    {\n        Do();\n        // after the call\n    }\n}"));
    }

    [Test]
    public async Task IgnoresBlankLinesAroundTheBlockButNotInsideIt()
    {
        await Run("\n\n    }\n\n    void Two()\n\n", "// between");

        Assert.That(_project.Read("A.cs"), Does.Contain("    void Two()\n    // between\n"));
    }

    [Test]
    public async Task ReportsWhereTheInsertionWent()
    {
        var result = await Run("void One()\n{", "// note");

        Assert.That(result, Does.StartWith("Inserted 1 line after line 4 of A.cs:"));
    }

    [Test]
    public void RefusesABlockThatAppearsTwiceAndNamesWhereItStarts()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("{\nDo();\n}", "x"));

        Assert.That(error!.Message, Is.EqualTo("The quoted lines appear 2 times (starting at line numbers 4, 9); quote lines that appear once."));
        Assert.That(_project.Read("A.cs"), Is.EqualTo(Source));
    }

    [Test]
    public void NamesTheFirstQuotedLineThatIsNotInTheFileAndTheClosestRealOne()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("void One()\n{\n    Do(1);\n}", "x"));

        Assert.That(error!.Message, Does.StartWith("Line 3 of the quoted lines is not in the file: Do(1);"));
        Assert.That(error.Message, Does.Contain("The closest line is line 5:         Do();").And.EndWith("Give just one existing line as 'line'."));
    }

    [Test]
    public void SaysWhenEveryQuotedLineExistsButNotConsecutively()
    {
        var error = Assert.ThrowsAsync<ToolException>(() => Run("void One()\nvoid Two()", "x"));

        Assert.That(error!.Message, Does.StartWith("Every quoted line is in the file, but not together as consecutive lines")
            .And.EndWith("Give just one of those lines as 'line'."));
    }

    [Test]
    public void RefusesAnAnchorLongerThanTheFile()
    {
        _project.Write("Short.cs", "a\nb\n");

        var error = Assert.ThrowsAsync<ToolException>(() => _tool.RunAsync(
            Json.Parse("""{"path":"Short.cs","line":"a\nb\nc","text":"x"}"""), CancellationToken.None));

        Assert.That(error!.Message, Does.StartWith("Line 3 of the quoted lines is not in the file: c"));
    }

    [Test]
    public async Task StillTreatsASingleLineWithBlankLinesAroundItAsOneLine()
    {
        await Run("\n  void One()  \n\n", "// note");

        Assert.That(_project.Read("A.cs"), Does.Contain("    void One()\n    // note\n    {"));
    }

    [Test]
    public async Task KeepsWindowsLineEndingsWhenAnchoredOnABlock()
    {
        _project.Write("W.cs", "one\r\ntwo\r\nthree\r\n");

        await _tool.RunAsync(Json.Parse("""{"path":"W.cs","line":"one\ntwo","text":"1.5"}"""), CancellationToken.None);

        Assert.That(_project.Read("W.cs"), Is.EqualTo("one\r\ntwo\r\n1.5\r\nthree\r\n"));
    }

    [Test]
    public void TheDescriptionOfTheLineParameterMentionsSeveralLines() =>
        Assert.That(_tool.Parameters["properties"]!["line"]!["description"]!.GetValue<string>(), Does.Contain("several consecutive lines"));
}
