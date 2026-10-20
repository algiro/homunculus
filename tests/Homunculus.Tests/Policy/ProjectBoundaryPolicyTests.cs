using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;
using NSubstitute;

namespace Homunculus.Tests.Policy;

public class ProjectBoundaryPolicyTests
{
    private TempProject _project = null!;
    private IPolicy _inner = null!;
    private ProjectBoundaryPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _inner = Substitute.For<IPolicy>();
        _inner.EvaluateAsync(default!, default, default).ReturnsForAnyArgs(Verdict.Deny);
        _policy = new ProjectBoundaryPolicy(_project.Location, _inner);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<Verdict> Evaluate(ITool tool, string json) =>
        _policy.EvaluateAsync(tool, Json.Parse(json), CancellationToken.None);

    [Test]
    public async Task AllowsPlans() => Assert.That(await Evaluate(new PlanTool(), """{"steps":[]}"""), Is.EqualTo(Verdict.Allow));

    [Test]
    public async Task AllowsReadsInsideTheProject() =>
        Assert.That(await Evaluate(new ReadFileTool(_project.Location), """{"path":"a.txt"}"""), Is.EqualTo(Verdict.Allow));

    [Test]
    public async Task AllowsWritesInsideTheProject() =>
        Assert.That(await Evaluate(new EditFileTool(_project.Location), """{"path":"src/a.txt"}"""), Is.EqualTo(Verdict.Allow));

    [Test]
    public async Task AllowsListingTheProjectRoot() =>
        Assert.That(await Evaluate(new ListFilesTool(_project.Location), "{}"), Is.EqualTo(Verdict.Allow));

    [Test]
    public async Task AsksTheWrappedPolicyForReadsOutsideTheProject() =>
        Assert.That(await Evaluate(new ReadFileTool(_project.Location), """{"path":"../secret.txt"}"""), Is.EqualTo(Verdict.Deny));

    [Test]
    public async Task AsksTheWrappedPolicyForWritesOutsideTheProject() =>
        Assert.That(await Evaluate(new DeleteFileTool(_project.Location), """{"path":"../secret.txt"}"""), Is.EqualTo(Verdict.Deny));

    [Test]
    public async Task AsksTheWrappedPolicyForCommands() =>
        Assert.That(await Evaluate(new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1)), """{"command":"echo hi"}"""),
            Is.EqualTo(Verdict.Deny));

    [Test]
    public async Task DoesNotBotherTheWrappedPolicyForSafeCalls()
    {
        await Evaluate(new PlanTool(), """{"steps":[]}""");

        await _inner.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default, default);
    }
}
