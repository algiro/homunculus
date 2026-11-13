using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class PlanToolTests
{
    private readonly Plan _plan = new();
    private readonly PlanTool _tool;

    public PlanToolTests() => _tool = new PlanTool(_plan);

    private Task<string> Run(string json) => _tool.RunAsync(Json.Parse(json), CancellationToken.None);

    [Test]
    public async Task ReturnsTheStepsNumbered() =>
        Assert.That(await Run("""{"steps":["read","edit"]}"""), Is.EqualTo("1. read\n2. edit"));

    [Test]
    public async Task AcceptsAnEmptyPlan() =>
        Assert.That(await Run("""{"steps":[]}"""), Is.Empty);

    [Test]
    public void RefusesTooManySteps()
    {
        var steps = string.Join(',', Enumerable.Range(0, Limits.PlanSteps + 1).Select(i => $"\"s{i}\""));

        Assert.ThrowsAsync<ToolException>(() => Run($$"""{"steps":[{{steps}}]}"""));
    }

    [Test]
    public void RefusesAStepThatIsTooLong() =>
        Assert.ThrowsAsync<ToolException>(() =>
            Run($$"""{"steps":["{{new string('x', Limits.PlanStepChars + 1)}}"]}"""));

    [Test]
    public async Task AcceptsAStepOfExactlyTheMaximumLength() =>
        Assert.That(await Run($$"""{"steps":["{{new string('x', Limits.PlanStepChars)}}"]}"""), Does.StartWith("1. x"));

    [Test]
    public void FailsWhenStepsAreMissing() => Assert.ThrowsAsync<ToolException>(() => Run("{}"));

    [Test]
    public void FailsWhenStepsIsNotAList() => Assert.ThrowsAsync<ToolException>(() => Run("""{"steps":"read"}"""));

    [Test]
    public void FailsCleanlyWhenAStepIsNotText() => Assert.ThrowsAsync<ToolException>(() => Run("""{"steps":[1,2]}"""));

    [Test]
    public void FailsWhenTheArgumentsAreNotAnObject() => Assert.ThrowsAsync<ToolException>(() => Run("[]"));

    [Test]
    public void IsAPlanWithoutAFileTarget() =>
        Assert.That(_tool.EffectOf(Json.Parse("{}")), Is.EqualTo(ToolEffect.Planning));

    [Test]
    public async Task KeepsTheStepsOfAValidCall()
    {
        await Run("""{"steps":["[x] read","[ ] edit"]}""");

        Assert.That(_plan.Steps, Is.EqualTo(new[] { "[x] read", "[ ] edit" }));
    }

    [Test]
    public async Task ReplacesTheEarlierPlanWithTheLatestOne()
    {
        await Run("""{"steps":["a","b"]}""");
        await Run("""{"steps":["c"]}""");

        Assert.That(_plan.Steps, Is.EqualTo(new[] { "c" }));
    }

    [Test]
    public async Task KeepsTheEarlierPlanWhenACallIsInvalid()
    {
        await Run("""{"steps":["a"]}""");

        Assert.ThrowsAsync<ToolException>(() => Run($$"""{"steps":["{{new string('x', Limits.PlanStepChars + 1)}}"]}"""));

        Assert.That(_plan.Steps, Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void AsksTheModelToMarkFinishedStepsInItsDescription() =>
        Assert.That(_tool.Description, Does.Contain("[x]").And.Contain("[ ]"));
}
