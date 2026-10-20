using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;
using NSubstitute;

namespace Homunculus.Tests.Policy;

public class SwitchablePolicyTests
{
    private IPolicy _strict = null!;
    private IPolicy _relaxed = null!;
    private SwitchablePolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _strict = Substitute.For<IPolicy>();
        _relaxed = Substitute.For<IPolicy>();
        _policy = new SwitchablePolicy(new Dictionary<string, IPolicy> { ["strict"] = _strict, ["relaxed"] = _relaxed });
    }

    private Task<Verdict> Evaluate() =>
        _policy.EvaluateAsync(new PlanTool(), Json.Parse("{}"), CancellationToken.None);

    [Test]
    public void StartsInTheFirstMode() => Assert.That(_policy.Mode, Is.EqualTo("strict"));

    [Test]
    public void ListsTheModes() => Assert.That(_policy.Modes, Is.EqualTo(new[] { "strict", "relaxed" }));

    [Test]
    public async Task AsksTheSelectedPolicy()
    {
        _relaxed.EvaluateAsync(default!, default, default).ReturnsForAnyArgs(Verdict.Allow);
        _policy.TrySetMode("relaxed");

        Assert.That(await Evaluate(), Is.EqualTo(Verdict.Allow));
        await _strict.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default, default);
    }

    [Test]
    public void SwitchesToAKnownMode()
    {
        Assert.That(_policy.TrySetMode("relaxed"), Is.True);
        Assert.That(_policy.Mode, Is.EqualTo("relaxed"));
    }

    [Test]
    public void KeepsTheModeWhenAskedForAnUnknownOne()
    {
        Assert.That(_policy.TrySetMode("bogus"), Is.False);
        Assert.That(_policy.Mode, Is.EqualTo("strict"));
    }
}
