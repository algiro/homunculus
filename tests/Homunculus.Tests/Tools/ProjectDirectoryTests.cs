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

    [Test]
    public void Contains_RejectsASiblingWhoseNameStartsWithTheProjectName()
    {
        var sibling = _project.Root + "-evil";

        Assert.That(_project.Location.Contains(Path.Combine(sibling, "a.txt")), Is.False);
    }
}
