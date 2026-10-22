using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Llm;

public class MeteredModelTests
{
    private static readonly FixedConversation Question = new([Message.User("hi")]);

    private static Task<Reply> Ask(MeteredModel model) => model.CompleteAsync(Question, [], CancellationToken.None);

    [Test]
    public async Task RecordsTheReportedUsageAndReturnsTheReplyUnchanged()
    {
        var usage = new TokenUsage(10, 5, 15);
        var ledger = new UsageLedger();
        var metered = new MeteredModel(new ScriptedModel(Replies.Text("hello")) { Usage = usage }, ledger, "agent");

        var reply = await Ask(metered);

        Assert.That(reply, Is.EqualTo(new Reply(Replies.Text("hello"), usage)));
        Assert.That(ledger.Sources.Single(), Is.EqualTo(new SourceUsage("agent", 1, usage)));
    }

    [Test]
    public async Task RecordsNothingWhenTheServerReportsNoUsage()
    {
        var ledger = new UsageLedger();

        await Ask(new MeteredModel(new ScriptedModel(Replies.Text("hello")), ledger, "agent"));

        Assert.That(ledger.Sources, Is.Empty);
    }

    [Test]
    public void RecordsNothingWhenTheCallFails()
    {
        var ledger = new UsageLedger();
        var metered = new MeteredModel(new FailingModel(), ledger, "agent");

        Assert.ThrowsAsync<ModelException>(() => Ask(metered));
        Assert.That(ledger.Sources, Is.Empty);
    }

    private sealed class FailingModel : ILanguageModel
    {
        public Task<Reply> CompleteAsync(IConversation conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation) =>
            throw new ModelException("down");
    }
}
