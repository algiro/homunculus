using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class PlanToolTests
{
    private readonly PlanTool _tool = new();

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

    [Test, Ignore("Known bug: a non-string step throws InvalidOperationException instead of ToolException.")]
    public void FailsCleanlyWhenAStepIsNotText() => Assert.ThrowsAsync<ToolException>(() => Run("""{"steps":[1,2]}"""));

    [Test]
    public void IsAPlanWithoutAFileTarget()
    {
        Assert.That(_tool.Access, Is.EqualTo(ToolAccess.Plan));
        Assert.That(_tool.TargetPath(Json.Parse("{}")), Is.Null);
    }
}
