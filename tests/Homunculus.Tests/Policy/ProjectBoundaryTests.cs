using Homunculus.Policy;
using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Policy;

public class ProjectBoundaryTests
{
    private TempProject _project = null!;
    private ProjectBoundary _boundary = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _boundary = new ProjectBoundary(_project.Location);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    private bool IsSafe(ITool tool, string json) => _boundary.IsSafe(tool, Json.Parse(json));

    [Test]
    public void PlansAreSafe() => Assert.That(IsSafe(new PlanTool(), """{"steps":[]}"""), Is.True);

    [Test]
    public void ReadsInsideTheProjectAreSafe() =>
        Assert.That(IsSafe(new ReadFileTool(_project.Location), """{"path":"a.txt"}"""), Is.True);

    [Test]
    public void WritesInsideTheProjectAreSafe() =>
        Assert.That(IsSafe(new EditFileTool(_project.Location), """{"path":"src/a.txt"}"""), Is.True);

    [Test]
    public void ListingTheProjectRootIsSafe() =>
        Assert.That(IsSafe(new ListFilesTool(_project.Location), "{}"), Is.True);

    [Test]
    public void ReadsOutsideTheProjectAreNotSafe() =>
        Assert.That(IsSafe(new ReadFileTool(_project.Location), """{"path":"../secret.txt"}"""), Is.False);

    [Test]
    public void WritesOutsideTheProjectAreNotSafe() =>
        Assert.That(IsSafe(new DeleteFileTool(_project.Location), """{"path":"../secret.txt"}"""), Is.False);

    [Test]
    public void CommandsAreNeverSafe() =>
        Assert.That(IsSafe(new RunCommandTool(_project.Location, TimeSpan.FromSeconds(1)), """{"command":"echo hi"}"""), Is.False);
}
