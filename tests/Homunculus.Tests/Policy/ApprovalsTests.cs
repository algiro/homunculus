using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Policy;

public class ApprovalsTests
{
    private TestApp _app = null!;

    [TearDown] public void TearDown() => _app.Dispose();

    private ITool Tool(string name) => _app.Tools.Find(name)!;

    private Task<string?> Check(string tool, string json) =>
        _app.Approvals.CheckAsync(Tool(tool), Json.Parse(json), CancellationToken.None);

    private const string Command = """{"command":"echo hi"}""";

    [Test]
    public async Task LetsCallsInsideTheProjectRunWithoutAskingAnyone()
    {
        _app = new TestApp(new ScriptedModel(), "effect");

        Assert.That(await Check("read_file", """{"path":"a.txt"}"""), Is.Null);
        Assert.That(_app.Ui.Questions, Is.Empty);
    }

    [Test]
    public async Task AllowAllNeverAsks()
    {
        _app = new TestApp(new ScriptedModel(), "allow-all");

        Assert.That(await Check("run_command", Command), Is.Null);
        Assert.That(_app.Ui.Questions, Is.Empty);
    }

    [Test]
    public async Task EffectModeAsksTheUserAndAcceptsYes()
    {
        _app = new TestApp(new ScriptedModel(), "effect");
        _app.Ui.Answers('y');

        Assert.That(await Check("run_command", Command), Is.Null);
        Assert.That(_app.Ui.Questions.Single(), Does.Contain("run_command"));
    }

    [Test]
    public async Task EffectModeRefusesWhenTheUserSaysNo()
    {
        _app = new TestApp(new ScriptedModel(), "effect");
        _app.Ui.Answers('n');

        Assert.That(await Check("run_command", Command), Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task AlwaysRemembersTheToolAndStopsAsking()
    {
        _app = new TestApp(new ScriptedModel(), "effect");
        _app.Ui.Answers('a');

        await Check("run_command", Command);
        var second = await Check("run_command", """{"command":"echo again"}""");

        Assert.That(second, Is.Null);
        Assert.That(_app.Ui.Questions, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AlwaysOnlyAppliesToThatTool()
    {
        _app = new TestApp(new ScriptedModel(), "effect");
        _app.Ui.Answers('a', 'n');

        await Check("run_command", Command);
        var other = await Check("delete_file", """{"path":"../outside.txt"}""");

        Assert.That(other, Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task JudgeModeAllowsWhenTheJudgeSaysAllow()
    {
        _app = new TestApp(new ScriptedModel(), "judge", new ScriptedModel(Reply.Text("allow")));

        Assert.That(await Check("run_command", Command), Is.Null);
        Assert.That(_app.Ui.Questions, Is.Empty);
    }

    [Test]
    public async Task JudgeModeDeniesWhenTheJudgeSaysDeny()
    {
        _app = new TestApp(new ScriptedModel(), "judge", new ScriptedModel(Reply.Text("deny")));

        Assert.That(await Check("run_command", Command), Is.EqualTo("Denied by the policy judge."));
        Assert.That(_app.Ui.Questions, Is.Empty);
    }

    [Test]
    public async Task JudgeModeAsksTheUserWhenTheJudgeIsUnsure()
    {
        _app = new TestApp(new ScriptedModel(), "judge", new ScriptedModel(Reply.Text("ask")));
        _app.Ui.Answers('y');

        Assert.That(await Check("run_command", Command), Is.Null);
        Assert.That(_app.Ui.Questions, Has.Count.EqualTo(1));
    }

    [Test]
    public void StartsInTheFirstConfiguredMode()
    {
        _app = new TestApp(new ScriptedModel(), "effect");

        Assert.That(_app.Approvals.Mode, Is.EqualTo("effect"));
    }

    [Test]
    public void SwitchesToAKnownMode()
    {
        _app = new TestApp(new ScriptedModel(), "effect");

        Assert.That(_app.Approvals.TrySetMode("allow-all"), Is.True);
        Assert.That(_app.Approvals.Mode, Is.EqualTo("allow-all"));
    }

    [Test]
    public void KeepsTheModeWhenAskedForAnUnknownOne()
    {
        _app = new TestApp(new ScriptedModel(), "effect");

        Assert.That(_app.Approvals.TrySetMode("bogus"), Is.False);
        Assert.That(_app.Approvals.Mode, Is.EqualTo("effect"));
    }
}
