using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Policy;

public class JudgePolicyTests
{
    private static readonly ITool Command = new RunCommandTool(new ProjectDirectory("."), TimeSpan.FromSeconds(1));

    private static Task<Verdict> Judge(ScriptedModel model) =>
        new JudgePolicy(model).EvaluateAsync(Command, Json.Parse("""{"command":"rm -rf /"}"""), CancellationToken.None);

    [TestCase("allow", Verdict.Allow)]
    [TestCase("Allow.", Verdict.Allow)]
    [TestCase("  ALLOW  ", Verdict.Allow)]
    [TestCase("deny", Verdict.Deny)]
    [TestCase("Deny: destructive", Verdict.Deny)]
    [TestCase("ask", Verdict.Ask)]
    [TestCase("I am not sure", Verdict.Ask)]
    [TestCase("", Verdict.Ask)]
    public async Task TurnsTheAnswerIntoAVerdict(string answer, Verdict expected) =>
        Assert.That(await Judge(new ScriptedModel(Replies.Text(answer))), Is.EqualTo(expected));

    [Test]
    public async Task AsksWhenTheJudgeSaysNothing() =>
        Assert.That(await Judge(new ScriptedModel(new Message(Role.Assistant))), Is.EqualTo(Verdict.Ask));

    [Test]
    public async Task ShowsTheJudgeTheToolCallWithoutOfferingTools()
    {
        var model = new ScriptedModel(Replies.Text("ask"));

        await Judge(model);

        var question = model.LastConversation[^1].Content;
        Assert.That(model.LastConversation[0].Role, Is.EqualTo(Role.System));
        Assert.That(question, Does.Contain("run_command").And.Contain("rm -rf /"));
        Assert.That(model.ToolLists[0], Is.Empty);
    }
}
