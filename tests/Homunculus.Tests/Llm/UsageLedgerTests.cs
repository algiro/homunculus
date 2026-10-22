using Homunculus.Llm;

namespace Homunculus.Tests.Llm;

public class UsageLedgerTests
{
    [Test]
    public void StartsEmpty() => Assert.That(new UsageLedger().Sources, Is.Empty);

    [Test]
    public void AddsUpTheCallsOfOneSource()
    {
        var ledger = new UsageLedger();

        ledger.Record("agent", new TokenUsage(10, 5, 15, Cached: 4));
        ledger.Record("agent", new TokenUsage(20, 1, 21, Cached: 6));

        Assert.That(ledger.Sources.Single(), Is.EqualTo(new SourceUsage("agent", 2, new TokenUsage(30, 6, 36, Cached: 10))));
    }

    [Test]
    public void KeepsSourcesApartInNameOrder()
    {
        var ledger = new UsageLedger();

        ledger.Record("judge", new TokenUsage(1, 1, 2));
        ledger.Record("agent", new TokenUsage(10, 5, 15));

        Assert.That(ledger.Sources.Select(s => (s.Source, s.Calls, s.Tokens.Total)),
            Is.EqualTo(new[] { ("agent", 1, 15), ("judge", 1, 2) }));
    }
}
