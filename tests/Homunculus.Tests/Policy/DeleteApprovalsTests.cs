using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;
using NSubstitute;

namespace Homunculus.Tests.Policy;

public class DeleteApprovalsTests
{
    private TempProject _project = null!;
    private FakeUserInterface _ui = null!;
    private ChangedFiles _changes = null!;
    private IPolicy _policy = null!;
    private Approvals _approvals = null!;
    private DeleteFileTool _delete = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _ui = new FakeUserInterface();
        _changes = new ChangedFiles(_project.Location);
        _policy = Substitute.For<IPolicy>();
        _policy.EvaluateAsync(default!, default, default).ReturnsForAnyArgs(Verdict.Allow);
        _approvals = new Approvals(_policy, _ui, _project.Location, _changes);
        _delete = new DeleteFileTool(_project.Location, _changes);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private Task<string?> Check(string path) =>
        _approvals.CheckAsync(_delete, Json.Parse($$"""{"path":"{{path}}"}"""), CancellationToken.None);

    [Test]
    public async Task AsksAboutAFileTheAgentDidNotCreateEvenWhenThePolicyAllows()
    {
        _ui.Answers('y');

        Assert.That(await Check("App.cs"), Is.Null);
        Assert.That(_ui.Questions.Single(), Does.Contain("delete_file").And.Contain("App.cs"));
        await _policy.DidNotReceiveWithAnyArgs().EvaluateAsync(default!, default, default);
    }

    [Test]
    public async Task RefusesWhenTheUserSaysNo()
    {
        _ui.Answers('n');

        Assert.That(await Check("App.cs"), Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task LetsTheAgentDeleteWhatItCreatedItself()
    {
        _changes.Created(_project.PathOf("Scratch.cs"));

        Assert.That(await Check("Scratch.cs"), Is.Null);
        Assert.That(_ui.Questions, Is.Empty);
    }

    [Test]
    public async Task AsksAboutADeletionOutsideTheProjectWithTheOutsideQuestion()
    {
        _changes.Created(Path.Combine(_project.Outside, "other.txt"));
        _ui.Answers('n');

        Assert.That(await Check("../other.txt"), Is.EqualTo("Refused by the user."));
        Assert.That(_ui.Questions.Single(), Does.Contain("OUTSIDE"));
    }

    [Test]
    public async Task AsksWhenNoRecordOfCreatedFilesIsKept()
    {
        var approvals = new Approvals(_policy, _ui, _project.Location);
        _ui.Answers('n');

        var result = await approvals.CheckAsync(_delete, Json.Parse("""{"path":"Scratch.cs"}"""), CancellationToken.None);

        Assert.That(result, Is.EqualTo("Refused by the user."));
    }

    [Test]
    public async Task TheBoundaryPolicyTreatsADeletionInsideTheProjectAsSafeAndOutsideAsNot()
    {
        var inner = Substitute.For<IPolicy>();
        inner.EvaluateAsync(default!, default, default).ReturnsForAnyArgs(Verdict.Deny);
        var boundary = new ProjectBoundaryPolicy(_project.Location, inner);

        Assert.That(await boundary.EvaluateAsync(_delete, Json.Parse("""{"path":"a.txt"}"""), CancellationToken.None), Is.EqualTo(Verdict.Allow));
        Assert.That(await boundary.EvaluateAsync(_delete, Json.Parse("""{"path":"../a.txt"}"""), CancellationToken.None), Is.EqualTo(Verdict.Deny));
    }
}
