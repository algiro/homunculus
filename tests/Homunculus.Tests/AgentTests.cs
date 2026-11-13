using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests;

/// <summary>The agent on its own: a scripted model and a fake user.</summary>
public class AgentTests
{
    private const string SystemPrompt = "be brief";

    private FakeUserInterface _ui = null!;

    [SetUp]
    public void SetUp() => _ui = new FakeUserInterface();

    private Agent Build(ScriptedModel model, bool showReasoning = true) =>
        new(model, new Conversation(SystemPrompt), showReasoning, _ui);

    private static Task Ask(Agent agent, string input = "do it") => agent.RespondAsync(input, CancellationToken.None);

    [Test]
    public async Task ShowsATextReplyAndStops()
    {
        var agent = Build(new ScriptedModel(Replies.Text("hello")));

        await Ask(agent);

        Assert.That(_ui.Output, Is.EqualTo(new[] { "hello" }));
    }

    [Test]
    public async Task ShowsTheTokensOfTheLastCallAfterTheAnswer()
    {
        var usage = new TokenUsage(Input: 10, Output: 5, Total: 15);
        var model = new ScriptedModel(Replies.Text(""), Replies.Text("done")) { Usage = usage };

        await Ask(Build(model));

        Assert.That(_ui.Usages, Is.EqualTo(new[] { usage }));
    }

    [Test]
    public async Task ShowsNoTokensWhenTheServerReportsNone()
    {
        await Ask(Build(new ScriptedModel(Replies.Text("hello"))));

        Assert.That(_ui.Usages, Is.Empty);
    }

    [Test]
    public async Task ShowsTheReasoningWhenEnabled()
    {
        var model = new ScriptedModel(Replies.Text("answer")) { Reasoning = "let me think" };

        await Ask(Build(model));

        Assert.That(_ui.Reasonings, Is.EqualTo(new[] { "let me think" }));
        Assert.That(_ui.Output[^1], Is.EqualTo("answer"));
    }

    [Test]
    public async Task HidesTheReasoningWhenDisabled()
    {
        var model = new ScriptedModel(Replies.Text("answer")) { Reasoning = "let me think" };

        await Ask(Build(model, showReasoning: false));

        Assert.That(_ui.Reasonings, Is.Empty);
        Assert.That(_ui.Output[^1], Is.EqualTo("answer"));
    }

    [Test]
    public async Task RemindsTheModelOnceWhenItReturnsNothingAndKeepsTheEmptyMessageOut()
    {
        var model = new ScriptedModel(Replies.Text(""), Replies.Text("answer"));

        await Ask(Build(model));

        Assert.That(model.Calls, Is.EqualTo(2));
        Assert.That(model.LastConversation.Select(m => m.Role), Is.EqualTo(new[] { Role.System, Role.User, Role.User }));
        Assert.That(_ui.Output[^1], Is.EqualTo("answer"));
    }

    [Test]
    public async Task AsksForAShortAnswerOnceWhenAReplyIsCutOffAndKeepsItOut()
    {
        var model = new ScriptedModel(Replies.CutOff, Replies.Text("answer"));

        await Ask(Build(model));

        Assert.That(model.Calls, Is.EqualTo(2));
        Assert.That(model.LastConversation.Select(m => m.Role), Is.EqualTo(new[] { Role.System, Role.User, Role.User }));
        Assert.That(model.LastConversation[^1].Content, Does.Contain("cut off"));
        Assert.That(_ui.Output[^1], Is.EqualTo("answer"));
    }

    [Test]
    public async Task StopsWhenASecondReplyIsCutOff()
    {
        var model = new ScriptedModel(Replies.CutOff, Replies.CutOff);

        await Ask(Build(model));

        Assert.That(model.Calls, Is.EqualTo(2));
        Assert.That(_ui.Output[^1], Does.StartWith("Stopped: the model's reply was cut off twice"));
    }

    [Test]
    public async Task GivesUpWhenTheModelReturnsNothingAgain()
    {
        var model = new ScriptedModel(Replies.Text(""), Replies.Text("   "));

        await Ask(Build(model));

        Assert.That(model.Calls, Is.EqualTo(2));
        Assert.That(_ui.Output, Is.EqualTo(new[] { "The model returned no answer." }));
    }

    [Test]
    public async Task OnlyRemindsOncePerMessage()
    {
        var model = ScriptedModel.Repeating(Replies.Text(""));
        var agent = Build(model);

        await Ask(agent, "one");
        await Ask(agent, "two");

        Assert.That(model.Calls, Is.EqualTo(4));
        Assert.That(_ui.Output, Is.EqualTo(new[] { "The model returned no answer.", "The model returned no answer." }));
    }

    [Test]
    public async Task SendsTheSystemPromptAndTheUserMessage()
    {
        var model = new ScriptedModel(Replies.Text("ok"));

        await Ask(Build(model), "fix the bug");

        Assert.That(model.LastConversation.Select(m => (m.Role, m.Content)),
            Is.EqualTo(new[] { (Role.System, (string?)SystemPrompt), (Role.User, "fix the bug") }));
    }

    [Test]
    public async Task ShowsTheModelTimeAfterEveryCall()
    {
        var model = new ScriptedModel(Replies.Text(""), Replies.Text("done"))
        {
            Elapsed = TimeSpan.FromSeconds(4.2),
        };

        await Ask(Build(model));

        Assert.That(_ui.Output.Count(line => line.EndsWith("model took 4.2s", StringComparison.Ordinal)), Is.EqualTo(2));
    }

    [Test]
    public async Task ShowsNoTimeWhenNothingMeasuredIt()
    {
        await Ask(Build(new ScriptedModel(Replies.Text("done"))));

        Assert.That(_ui.AllOutput, Does.Not.Contain("model took"));
    }

    [Test]
    public async Task RemembersTheConversationBetweenMessages()
    {
        var model = new ScriptedModel(Replies.Text("first"), Replies.Text("second"));
        var agent = Build(model);

        await Ask(agent, "one");
        await Ask(agent, "two");

        Assert.That(model.LastConversation.Select(m => m.Content), Is.EqualTo(new[] { SystemPrompt, "one", "first", "two" }));
    }

    [Test]
    public async Task ForgetsTheConversationWhenCleared()
    {
        var model = new ScriptedModel(Replies.Text("first"), Replies.Text("second"));
        var agent = Build(model);

        await Ask(agent, "one");
        agent.ClearConversation();
        await Ask(agent, "two");

        Assert.That(model.LastConversation.Select(m => m.Content), Is.EqualTo(new[] { SystemPrompt, "two" }));
    }

    [Test]
    public void PropagatesCancellation()
    {
        var agent = Build(new ScriptedModel(Replies.Text("never")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => agent.RespondAsync("x", cancellation.Token));
    }
}
