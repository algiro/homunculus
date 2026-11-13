using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ToolRegistryTests
{
    private TestApp _app = null!;

    [SetUp] public void SetUp() => _app = new TestApp(new ScriptedModel());
    [TearDown] public void TearDown() => _app.Dispose();

    [Test]
    public void FindsToolsByName() => Assert.That(_app.Tools.Find("read_file"), Is.InstanceOf<ReadFileTool>());

    [Test]
    public void ReturnsNullForUnknownTools() => Assert.That(_app.Tools.Find("nope"), Is.Null);

    [Test]
    public void OffersTheThreeTools() =>
        Assert.That(_app.Tools.All.Select(t => t.Name), Is.EquivalentTo(new[] { "list_files", "glob", "read_file" }));

    [Test]
    public void DescribesEveryToolForTheModel()
    {
        var specs = _app.Tools.Specs();

        Assert.That(specs, Has.Count.EqualTo(3));
        Assert.That(specs, Has.All.Matches<Homunculus.Llm.ToolSpec>(s =>
            s.Description.Length > 0 && s.Parameters["type"]!.GetValue<string>() == "object"));
    }

    [Test]
    public void MarksRequiredParameters()
    {
        var read = _app.Tools.Specs().Single(s => s.Name == "read_file");

        var required = read.Parameters["required"]!.AsArray().Select(n => n!.GetValue<string>());

        Assert.That(required, Is.EqualTo(new[] { "path" }));
    }

    [Test]
    public void ListFilesHasNoRequiredParameters()
    {
        var list = _app.Tools.Specs().Single(s => s.Name == "list_files");

        Assert.That(list.Parameters["required"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void LoadsOnlyTheEnabledTools()
    {
        using var app = new TestApp(new ScriptedModel(), settings: TestApp.DefaultSettings with { EnabledTools = ["read_file"] });

        Assert.That(app.Tools.All.Select(t => t.Name), Is.EquivalentTo(new[] { "read_file" }));
    }

    [Test]
    public void RefusesAnUnknownToolAtStartup() =>
        Assert.Throws<InvalidOperationException>(() =>
            new TestApp(new ScriptedModel(), settings: TestApp.DefaultSettings with { EnabledTools = ["teleport"] }));
}
