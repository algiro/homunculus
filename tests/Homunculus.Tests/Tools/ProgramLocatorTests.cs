using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ProgramLocatorTests
{
    private TempProject _project = null!;
    private string _bin = null!;

    [SetUp]
    public void SetUp()
    {
        _project = new TempProject();
        _bin = Path.Combine(_project.Outside, "bin");
        Directory.CreateDirectory(_bin);
    }

    [TearDown] public void TearDown() => _project.Dispose();

    /// <summary>An empty file that the system would treat as the program <paramref name="name"/>.</summary>
    private static string FakeProgram(string folder, string name, string? extension = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + (extension ?? (OperatingSystem.IsWindows() ? ".exe" : "")));
        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private ProgramLocator Locator(params string[] path) => new(_project.Location, string.Join(Path.PathSeparator, path));

    [Test]
    public void FindsAProgramOnPath()
    {
        var git = FakeProgram(_bin, "git");

        Assert.That(Locator(_bin).Find("git"), Is.EqualTo(git));
    }

    [Test]
    public void NeverFindsAProgramInsideTheProject()
    {
        FakeProgram(_project.Root, "git");
        FakeProgram(_project.PathOf("tools"), "git");

        Assert.That(Locator(_project.Root, _project.PathOf("tools")).Find("git"), Is.Null);
    }

    [Test]
    public void SkipsTheProjectAndFindsTheRealProgramFurtherAlongPath()
    {
        FakeProgram(_project.Root, "git");
        var real = FakeProgram(_bin, "git");

        Assert.That(Locator(_project.Root, _bin).Find("git"), Is.EqualTo(real));
    }

    [Test]
    public void SkipsRelativePathEntriesSuchAsTheCurrentFolder()
    {
        FakeProgram(Path.Combine(Directory.GetCurrentDirectory()), "homunculus-fake-tool");
        try
        {
            Assert.That(Locator(".", "").Find("homunculus-fake-tool"), Is.Null);
        }
        finally
        {
            File.Delete(Path.Combine(Directory.GetCurrentDirectory(), "homunculus-fake-tool" + (OperatingSystem.IsWindows() ? ".exe" : "")));
        }
    }

    [TestCase("../git")]
    [TestCase("bin/git")]
    public void FindsNothingForAPath(string program)
    {
        FakeProgram(_bin, "git");

        Assert.That(Locator(_bin).Find(program), Is.Null);
    }

    [Test]
    public void NeverRunsABatchFileOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Batch files are a Windows matter.");
        FakeProgram(_bin, "npm", ".cmd");
        FakeProgram(_bin, "npm", ".bat");

        Assert.That(Locator(_bin).Find("npm"), Is.Null);
    }

    [Test]
    public void SkipsAFileThatIsNotExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows has no execute permission.");
            return;
        }

        var path = FakeProgram(_bin, "git");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.That(Locator(_bin).Find("git"), Is.Null);
    }
}
