using Homunculus.Tools;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Tools;

public class ProjectDirectoryTests
{
    private TempProject _project = null!;

    [SetUp] public void SetUp() => _project = new TempProject();
    [TearDown] public void TearDown() => _project.Dispose();

    [Test]
    public void Resolve_PlacesRelativePathsUnderTheRoot() =>
        Assert.That(_project.Location.Resolve("src/a.txt"), Is.EqualTo(Path.Combine(_project.Root, "src", "a.txt")));

    [Test]
    public void Resolve_KeepsAbsolutePaths() =>
        Assert.That(_project.Location.Resolve(_project.Outside), Is.EqualTo(_project.Outside));

    [TestCase(".")]
    [TestCase("a.txt")]
    [TestCase("deep/er/a.txt")]
    [TestCase("a/../b.txt")]
    public void Contains_PathsInsideTheProject(string relative) =>
        Assert.That(_project.Location.Contains(_project.Location.Resolve(relative)), Is.True);

    [TestCase("..")]
    [TestCase("../other.txt")]
    [TestCase("a/../../other.txt")]
    public void Contains_RejectsEscapesThroughParentFolders(string relative) =>
        Assert.That(_project.Location.Contains(_project.Location.Resolve(relative)), Is.False);

    [Test]
    public void Contains_RejectsAbsolutePathsOutside() =>
        Assert.That(_project.Location.Contains(_project.Outside), Is.False);

    /// <summary>A folder outside the project holding a file, the kind of place a link inside the project can lead to.</summary>
    private string Secrets()
    {
        var secrets = Path.Combine(_project.Outside, "secrets");
        Directory.CreateDirectory(secrets);
        File.WriteAllText(Path.Combine(secrets, "passwd"), "root:x:0:0");
        return secrets;
    }

    [TestCase("link")]
    [TestCase("link/passwd")]
    [TestCase("link/new-file.txt")]
    [TestCase("link/new-folder/new-file.txt")]
    public void Contains_RejectsWhatALinkedFolderLeadsToOutsideTheProject(string relative)
    {
        _project.LinkDirectory("link", Secrets());

        Assert.That(_project.Location.Contains(_project.Location.Resolve(relative)), Is.False);
    }

    [Test]
    public void Contains_RejectsALinkedFolderDeeperInTheProject()
    {
        _project.LinkDirectory("src/fixtures/link", Secrets());

        Assert.That(_project.Location.Contains(_project.Location.Resolve("src/fixtures/link/passwd")), Is.False);
    }

    [Test]
    public void Contains_RejectsALinkedFolderWhoseTargetDoesNotExistYet()
    {
        _project.LinkDirectory("link", Secrets());
        Directory.Delete(Path.Combine(_project.Outside, "secrets"), recursive: true);

        Assert.That(_project.Location.Contains(_project.Location.Resolve("link/new-file.txt")), Is.False);
    }

    [Test]
    public void Contains_RejectsALinkedFileThatPointsOutside()
    {
        _project.LinkFile("passwd", Path.Combine(Secrets(), "passwd"));

        Assert.That(_project.Location.Contains(_project.Location.Resolve("passwd")), Is.False);
    }

    [Test]
    public void Contains_RejectsADanglingFileLinkThatPointsOutside()
    {
        _project.LinkFile("new.txt", Path.Combine(_project.Outside, "created-outside.txt"));

        Assert.That(_project.Location.Contains(_project.Location.Resolve("new.txt")), Is.False);
    }

    [Test]
    public void Contains_AcceptsALinkThatStaysInsideTheProject()
    {
        _project.Write("real/a.txt", "a");
        _project.LinkDirectory("alias", _project.PathOf("real"));

        Assert.That(_project.Location.Contains(_project.Location.Resolve("alias/a.txt")), Is.True);
    }

    [Test]
    public void Contains_StillWorksWhenTheProjectIsOpenedThroughALink()
    {
        var viaLink = Path.Combine(_project.Outside, "project-link");
        try
        {
            Directory.CreateSymbolicLink(viaLink, _project.Root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"This system does not let the tests create a directory symbolic link: {e.Message}");
        }

        var project = new ProjectDirectory(viaLink);

        Assert.That(project.Contains(project.Resolve("src/a.txt")), Is.True);
        Assert.That(project.Contains(project.Resolve("../other.txt")), Is.False);
    }

    [Test]
    public void Contains_RejectsASiblingWhoseNameStartsWithTheProjectName()
    {
        var sibling = _project.Root + "-evil";

        Assert.That(_project.Location.Contains(Path.Combine(sibling, "a.txt")), Is.False);
    }
}
