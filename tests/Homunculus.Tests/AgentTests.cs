using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests;

/// <summary>The agent loop on its own: a scripted model, a fake tool and a fixed policy, with no project on disk.</summary>
public class AgentTests
{
    private const string SystemPrompt = "be brief";

    private FakeTool _echo = null!;
    private FakeUserInterface _ui = null!;

    [SetUp]
    public void SetUp()
    {
        _echo = new FakeTool("echo");
        _ui = new FakeUserInterface();
    }

    private Agent Build(ScriptedModel model, Verdict verdict = Verdict.Allow, IterationLimits? limits = null)
    {
        var tools = new ToolRegistry([_echo, new PlanTool()]);
        var runner = new ToolRunner(tools, new Approvals(new FixedPolicy(verdict), _ui), _ui);
        return new Agent(model, new Conversation(SystemPrompt), tools, runner, limits ?? new IterationLimits(8, 24), _ui);
    }

    private static Task Ask(Agent agent, string input = "do it") => agent.RespondAsync(input, CancellationToken.None);

    [Test]
    public async Task ShowsATextReplyAndStops()
    {
        var agent = Build(new ScriptedModel(Reply.Text("hello")));

        await Ask(agent);

        Assert.That(_ui.Output, Is.EqualTo(new[] { "hello" }));
    }

    [Test]
    public async Task SendsTheSystemPromptTheUserMessageAndTheToolSpecs()
    {
        var model = new ScriptedModel(Reply.Text("ok"));

        await Ask(Build(model), "fix the bug");

        Assert.That(model.LastConversation.Select(m => (m.Role, m.Content)),
            Is.EqualTo(new[] { (Role.System, (string?)SystemPrompt), (Role.User, "fix the bug") }));
        Assert.That(model.ToolLists[0].Select(t => t.Name), Is.EquivalentTo(new[] { "echo", "plan" }));
    }

    [Test]
    public async Task RunsARequestedToolAndReturnsTheResultToTheModel()
    {
        _echo.Result = "pong";
        var model = new ScriptedModel(Reply.Tool("echo", """{"n":1}""", "c1"), Reply.Text("it said pong"));

        await Ask(Build(model));

        Assert.That(_echo.Calls.Single().GetProperty("n").GetInt32(), Is.EqualTo(1));
        var toolMessage = model.Conversations[1][^1];
        Assert.That((toolMessage.Role, toolMessage.ToolCallId, toolMessage.Content), Is.EqualTo((Role.Tool, "c1", (string?)"pong")));
        Assert.That(_ui.Output[^1], Is.EqualTo("it said pong"));
    }

    [Test]
    public async Task AnswersEveryToolCallOfOneReplyInOrder()
    {
        var model = new ScriptedModel(
            Reply.Calls(
                new ToolCall("a", "echo", Json.Parse("""{"n":1}""")),
                new ToolCall("b", "echo", Json.Parse("""{"n":2}"""))),
            Reply.Text("done"));

        await Ask(Build(model));

        Assert.That(_echo.Calls.Select(c => c.GetProperty("n").GetInt32()), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(model.Conversations[1].Where(m => m.Role == Role.Tool).Select(m => m.ToolCallId), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public async Task KeepsGoingWhenTheModelAsksForAnUnknownTool()
    {
        var model = new ScriptedModel(Reply.Tool("fly"), Reply.Text("sorry"));

        await Ask(Build(model));

        Assert.That(model.Conversations[1][^1].Role, Is.EqualTo(Role.Tool));
        Assert.That(_ui.Output[^1], Is.EqualTo("sorry"));
    }

    [Test]
    public async Task KeepsGoingWhenAToolFails()
    {
        _echo.Failure = new ToolException("no good");
        var model = new ScriptedModel(Reply.Tool("echo"), Reply.Text("ok"));

        await Ask(Build(model));

        Assert.That(model.Conversations[1][^1].Content, Does.Contain("no good"));
        Assert.That(_ui.Output[^1], Is.EqualTo("ok"));
    }

    [Test]
    public async Task DoesNotRunAToolThePolicyDenies()
    {
        var model = new ScriptedModel(Reply.Tool("echo"), Reply.Text("understood"));

        await Ask(Build(model, Verdict.Deny));

        Assert.That(_echo.Calls, Is.Empty);
        Assert.That(model.Conversations[1][^1].Role, Is.EqualTo(Role.Tool));
        Assert.That(_ui.Output[^1], Is.EqualTo("understood"));
    }

    [Test]
    public async Task StopsAfterTheIterationLimit()
    {
        var model = ScriptedModel.Repeating(Reply.Tool("echo"));

        await Ask(Build(model, limits: new IterationLimits(3, 5)));

        Assert.That(model.Calls, Is.EqualTo(3));
        Assert.That(_ui.Output[^1], Does.Contain("iteration limit"));
    }

    [Test]
    public async Task AllowsMoreIterationsOnceAPlanWasMade()
    {
        var model = new ScriptedModel(
            [Reply.Tool("plan", """{"steps":["a"]}"""), .. Enumerable.Repeat(Reply.Tool("echo"), 10)]);

        await Ask(Build(model, limits: new IterationLimits(3, 5)));

        Assert.That(model.Calls, Is.EqualTo(5));
    }

    [Test]
    public async Task RemembersTheConversationBetweenMessages()
    {
        var model = new ScriptedModel(Reply.Text("first"), Reply.Text("second"));
        var agent = Build(model);

        await Ask(agent, "one");
        await Ask(agent, "two");

        Assert.That(model.LastConversation.Select(m => m.Content), Is.EqualTo(new[] { SystemPrompt, "one", "first", "two" }));
    }

    [Test]
    public async Task ForgetsTheConversationWhenCleared()
    {
        var model = new ScriptedModel(Reply.Text("first"), Reply.Text("second"));
        var agent = Build(model);

        await Ask(agent, "one");
        agent.ClearConversation();
        await Ask(agent, "two");

        Assert.That(model.LastConversation.Select(m => m.Content), Is.EqualTo(new[] { SystemPrompt, "two" }));
    }

    [Test]
    public void PropagatesCancellation()
    {
        var agent = Build(new ScriptedModel(Reply.Text("never")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => agent.RespondAsync("x", cancellation.Token));
    }
}
