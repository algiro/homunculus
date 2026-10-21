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
    public void OffersTheSixTools() =>
        Assert.That(_app.Tools.All.Select(t => t.Name), Is.EquivalentTo(
            new[] { "list_files", "read_file", "edit_file", "delete_file", "run_command", "plan" }));

    [Test]
    public void DescribesEveryToolForTheModel()
    {
        var specs = _app.Tools.Specs();

        Assert.That(specs, Has.Count.EqualTo(6));
        Assert.That(specs, Has.All.Matches<Homunculus.Llm.ToolSpec>(s =>
            s.Description.Length > 0 && s.Parameters["type"]!.GetValue<string>() == "object"));
    }

    [Test]
    public void MarksRequiredParameters()
    {
        var edit = _app.Tools.Specs().Single(s => s.Name == "edit_file");

        var required = edit.Parameters["required"]!.AsArray().Select(n => n!.GetValue<string>());

        Assert.That(required, Is.EqualTo(new[] { "path", "old_str", "new_str" }));
    }

    [Test]
    public void ListFilesHasNoRequiredParameters()
    {
        var list = _app.Tools.Specs().Single(s => s.Name == "list_files");

        Assert.That(list.Parameters["required"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void PlanTakesAListOfSteps()
    {
        var plan = _app.Tools.Specs().Single(s => s.Name == "plan");

        Assert.That(plan.Parameters["properties"]!["steps"]!["type"]!.GetValue<string>(), Is.EqualTo("array"));
    }

    [Test]
    public void LoadsOnlyTheEnabledToolsPlusPlan()
    {
        using var app = new TestApp(new ScriptedModel(), settings: TestApp.DefaultSettings with { EnabledTools = ["read_file"] });

        Assert.That(app.Tools.All.Select(t => t.Name), Is.EquivalentTo(new[] { "read_file", "plan" }));
    }

    [Test]
    public void RefusesAnUnknownToolAtStartup() =>
        Assert.Throws<InvalidOperationException>(() =>
            new TestApp(new ScriptedModel(), settings: TestApp.DefaultSettings with { EnabledTools = ["teleport"] }));
}
