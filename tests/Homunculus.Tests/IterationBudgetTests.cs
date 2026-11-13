using Homunculus.Tests.Support;

namespace Homunculus.Tests;

public class IterationBudgetTests
{
    private FakeUserInterface _ui = null!;

    [SetUp] public void SetUp() => _ui = new FakeUserInterface();

    private IterationBudget Budget(IterationLimits limits) => new(limits, _ui);

    private static int Take(IterationBudget budget, bool planned = false)
    {
        var taken = 0;
        while (budget.TryTakeTurn(planned)) taken++;
        return taken;
    }

    [Test]
    public void StopsAtTheLimitWhenTheUserDeclines()
    {
        _ui.Answers('n');
        var budget = Budget(new IterationLimits(3, 5));

        Assert.That(Take(budget), Is.EqualTo(3));
        Assert.That(_ui.Questions.Single(), Does.Contain("Reached 3"));
        Assert.That(budget.Used, Is.EqualTo(3));
    }

    [Test]
    public void GrantsAFullBatchForEachYes()
    {
        _ui.Answers('y', 'y', 'n');

        Assert.That(Take(Budget(new IterationLimits(3, 5))), Is.EqualTo(9));
        Assert.That(_ui.Questions, Has.Count.EqualTo(3));
    }

    [Test]
    public void NeverAsksWithoutALimit()
    {
        var budget = Budget(IterationLimits.Unlimited);

        for (var i = 0; i < 1000; i++) Assert.That(budget.TryTakeTurn(planned: false), Is.True);

        Assert.That(_ui.Questions, Is.Empty);
        Assert.That(budget.Used, Is.EqualTo(1000));
    }

    [Test]
    public void ARaisedLimitOnceAPlanExistsDefersTheQuestion()
    {
        _ui.Answers('n');
        var budget = Budget(new IterationLimits(3, 5));

        for (var i = 0; i < 2; i++) budget.TryTakeTurn(planned: false);
        var more = 0;
        while (budget.TryTakeTurn(planned: true)) more++;

        Assert.That(budget.Used, Is.EqualTo(5));
        Assert.That(more, Is.EqualTo(3));
    }
}
