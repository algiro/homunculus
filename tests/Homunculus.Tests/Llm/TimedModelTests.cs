using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Llm;

public class TimedModelTests
{
    /// <summary>A clock the test moves by hand, so no test ever sleeps.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    /// <summary>A model that takes a given time (on the manual clock) and then answers or fails.</summary>
    private sealed class SlowModel(ManualClock clock, TimeSpan takes, Exception? fails = null) : ILanguageModel
    {
        public Task<Reply> CompleteAsync(IConversation conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation)
        {
            clock.Advance(takes);
            return fails is null ? Task.FromResult(new Reply(Message.Assistant("hi", null))) : Task.FromException<Reply>(fails);
        }
    }

    private static readonly FixedConversation Conversation = new([Message.User("x")]);

    [Test]
    public async Task RecordsHowLongEachCallTookUnderTheSourceName()
    {
        var clock = new ManualClock();
        var ledger = new TimeLedger();
        var timed = new TimedModel(new SlowModel(clock, TimeSpan.FromSeconds(2.5)), ledger, "agent", clock);

        await timed.CompleteAsync(Conversation, [], CancellationToken.None);
        await timed.CompleteAsync(Conversation, [], CancellationToken.None);

        Assert.That(ledger.Sources.Single(), Is.EqualTo(new SourceTime("agent", 2, TimeSpan.FromSeconds(5))));
    }

    [Test]
    public async Task ReturnsTheInnerModelsReplyUnchanged()
    {
        var clock = new ManualClock();
        var timed = new TimedModel(new SlowModel(clock, TimeSpan.FromSeconds(1)), new TimeLedger(), "agent", clock);

        var reply = await timed.CompleteAsync(Conversation, [], CancellationToken.None);

        Assert.That(reply.Message.Content, Is.EqualTo("hi"));
    }

    [Test]
    public void RecordsTheTimeOfAFailedCallAndLetsTheErrorThrough()
    {
        var clock = new ManualClock();
        var ledger = new TimeLedger();
        var timed = new TimedModel(new SlowModel(clock, TimeSpan.FromSeconds(4), new ModelException("down")), ledger, "agent", clock);

        Assert.ThrowsAsync<ModelException>(() => timed.CompleteAsync(Conversation, [], CancellationToken.None));

        Assert.That(ledger.For("agent")!.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public async Task KeepsSourcesApartAndTotalsThem()
    {
        var clock = new ManualClock();
        var ledger = new TimeLedger();
        await new TimedModel(new SlowModel(clock, TimeSpan.FromSeconds(3)), ledger, "agent", clock).CompleteAsync(Conversation, [], CancellationToken.None);
        await new TimedModel(new SlowModel(clock, TimeSpan.FromSeconds(1)), ledger, "judge", clock).CompleteAsync(Conversation, [], CancellationToken.None);

        Assert.That(ledger.Sources.Select(s => s.Source), Is.EqualTo(new[] { "agent", "judge" }));
        Assert.That(ledger.Total, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public async Task StacksWithTheMeteredModelSoBothLedgersFill()
    {
        var clock = new ManualClock();
        var time = new TimeLedger();
        var usage = new UsageLedger();
        var inner = new ScriptedModel(Replies.Text("hi")) { Usage = new TokenUsage(10, 5, 15, 0) };
        var stacked = new MeteredModel(new TimedModel(inner, time, "agent", clock), usage, "agent");

        await stacked.CompleteAsync(Conversation, [], CancellationToken.None);

        Assert.That(time.For("agent")!.Calls, Is.EqualTo(1));
        Assert.That(usage.Sources.Single().Tokens.Total, Is.EqualTo(15));
    }
}
