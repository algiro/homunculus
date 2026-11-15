using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;
using NSubstitute;

namespace Homunculus.Tests.Policy;

public class ApprovalsTests
{
    private static readonly PlanTool Tool = new();
    private static readonly ITool OtherTool = new ListFilesTool(new ProjectDirectory("."));

    private IPolicy _policy = null!;
    private FakeUserInterface _ui = null!;
    private Approvals _approvals = null!;

    [SetUp]
    public void SetUp()
    {
        _policy = Substitute.For<IPolicy>();
        _ui = new FakeUserInterface();
        _approvals = new Approvals(_policy, _ui);
    }

    private void PolicyAnswers(Verdict verdict) =>
        _policy.EvaluateAsync(default!, default, default).ReturnsForAnyArgs(verdict);

    private Task<string?> Check(ITool tool) =>
        _approvals.CheckAsync(tool, Json.Parse("{}"), CancellationToken.None);

    [Test]
    public async Task LetsAnAllowedCallRunWithoutAsking()
    {
        PolicyAnswers(Verdict.Allow);

        Assert.That(await Check(Tool), Is.Null);
        Assert.That(_ui.Questions, Is.Empty);
    }

    [Test]
    public async Task RefusesADeniedCallWithoutAsking()
    {
        PolicyAnswers(Verdict.Deny);

        Assert.That(await Check(Tool), Is.EqualTo("Denied by the policy."));
        Assert.That(_ui.Questions, Is.Empty);
    }

    [Test]
    public async Task AsksTheUserWhenThePolicyIsUnsureAndAcceptsYes()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('y');

        Assert.That(await Check(Tool), Is.Null);
        Assert.That(_ui.Questions.Single(), Does.Contain(Tool.Name));
    }

    [Test]
    public async Task RefusesWhenTheUserSaysNo()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('n');

        Assert.That(await Check(Tool), Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task AlwaysRemembersTheToolAndStopsAskingEvenThePolicy()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('a');

        await Check(Tool);
        var second = await Check(Tool);

        Assert.That(second, Is.Null);
        Assert.That(_ui.Questions, Has.Count.EqualTo(1));
        await _policy.ReceivedWithAnyArgs(1).EvaluateAsync(default!, default, default);
    }

    [Test]
    public async Task AlwaysOnlyAppliesToThatTool()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('a', 'n');

        await Check(Tool);
        var other = await Check(OtherTool);

        Assert.That(other, Is.EqualTo("Refused by the user."));
    }

    private static readonly ProjectDirectory Project = new(Path.Combine(Path.GetTempPath(), "approvals-project"));
    private static readonly ReadFileTool Reader = new(Project);

    private Approvals Guarded() => new(_policy, _ui, Project);

    private static Task<string?> CheckRead(Approvals approvals, string path) =>
        approvals.CheckAsync(Reader, Json.Parse($$"""{"path":"{{path}}"}"""), CancellationToken.None);

    [Test]
    public async Task AsksTheUserForAnAccessOutsideTheProjectEvenWhenThePolicyAllows()
    {
        PolicyAnswers(Verdict.Allow);
        _ui.Answers('y');

        Assert.That(await CheckRead(Guarded(), "../secret.txt"), Is.Null);
        Assert.That(_ui.Questions.Single(), Does.Contain("OUTSIDE").And.Contain(Project.Root));
        await _policy.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default, default);
    }

    [Test]
    public async Task RefusesAnAccessOutsideTheProjectWhenTheUserSaysNo()
    {
        PolicyAnswers(Verdict.Allow);
        _ui.Answers('n');

        Assert.That(await CheckRead(Guarded(), "../secret.txt"), Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task KeepsAskingForEveryAccessOutsideTheProject()
    {
        PolicyAnswers(Verdict.Allow);
        _ui.Answers('y', 'y');
        var approvals = Guarded();

        await CheckRead(approvals, "../one.txt");
        await CheckRead(approvals, "../two.txt");

        Assert.That(_ui.Questions, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task AlwaysAllowingAToolDoesNotCoverAccessOutsideTheProject()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('a', 'n');
        var approvals = Guarded();

        Assert.That(await CheckRead(approvals, "inside.txt"), Is.Null);
        Assert.That(await CheckRead(approvals, "another-inside.txt"), Is.Null);
        Assert.That(await CheckRead(approvals, "../secret.txt"), Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task LeavesAccessInsideTheProjectToThePolicy()
    {
        PolicyAnswers(Verdict.Allow);

        Assert.That(await CheckRead(Guarded(), "src/a.txt"), Is.Null);
        Assert.That(_ui.Questions, Is.Empty);
    }

    private static readonly RunCommandTool Commands = new(new ProjectDirectory("."), TimeSpan.FromSeconds(1));

    private Approvals WithPrograms(params string[] allowed) => new(_policy, _ui, programs: new AllowedPrograms(allowed));

    private static Task<string?> CheckCommand(Approvals approvals, string program, params string[] args) =>
        approvals.CheckAsync(Commands, Json.Parse(System.Text.Json.JsonSerializer.Serialize(new { program, args })), CancellationToken.None);

    [Test]
    public async Task AsksBeforeRunningAProgramOffTheListEvenWhenThePolicyAllows()
    {
        PolicyAnswers(Verdict.Allow);
        _ui.Answers('y');

        Assert.That(await CheckCommand(WithPrograms("dotnet", "git"), "curl", "https://example.com"), Is.Null);
        Assert.That(_ui.Questions.Single(), Does.Contain("'curl'").And.Contain("NOT an allowed program").And.Contain("dotnet, git"));
        await _policy.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default, default);
    }

    [Test]
    public async Task RefusesAProgramOffTheListWhenTheUserSaysNo()
    {
        PolicyAnswers(Verdict.Allow);
        _ui.Answers('n');

        Assert.That(await CheckCommand(WithPrograms("dotnet"), "curl"), Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task NeverOffersAlwaysForAProgramOffTheListAndAsksEachTime()
    {
        PolicyAnswers(Verdict.Allow);
        _ui.Answers('y', 'y');
        var approvals = WithPrograms("dotnet");

        await CheckCommand(approvals, "curl", "x");
        await CheckCommand(approvals, "curl", "x");

        Assert.That(_ui.Options, Is.EqualTo(new[] { "yn", "yn" }));
    }

    [Test]
    public async Task LeavesAListedProgramToThePolicy()
    {
        PolicyAnswers(Verdict.Allow);

        Assert.That(await CheckCommand(WithPrograms("dotnet"), "dotnet", "test"), Is.Null);
        Assert.That(_ui.Questions, Is.Empty);
    }

    [Test]
    public async Task AlwaysForACommandCoversThatExactCommandOnly()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('a', 'n', 'n');
        var approvals = WithPrograms("dotnet");

        Assert.That(await CheckCommand(approvals, "dotnet", "test"), Is.Null);
        Assert.That(await CheckCommand(approvals, "dotnet", "test"), Is.Null);
        Assert.That(await CheckCommand(approvals, "dotnet", "test", "--filter", "x"), Is.EqualTo("Refused by the user."));
        Assert.That(await CheckCommand(approvals, "dotnet", "run"), Is.EqualTo("Refused by the user."));
        Assert.That(_ui.Questions, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SaysThatAlwaysForACommandMeansThatCommandOnly()
    {
        PolicyAnswers(Verdict.Ask);
        _ui.Answers('n');

        await CheckCommand(WithPrograms("dotnet"), "dotnet", "test");

        Assert.That(_ui.Questions.Single(), Does.Contain("always allow exactly this command"));
        Assert.That(_ui.Options.Single(), Is.EqualTo("yna"));
    }
}
