using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class AllowedProgramsTests
{
    private static readonly AllowedPrograms DotnetAndGit = new(["dotnet", " git ", ""]);

    [TestCase("dotnet")]
    [TestCase("git")]
    public void ContainsTheListedPrograms(string program) => Assert.That(DotnetAndGit.Contains(program), Is.True);

    [TestCase("curl")]
    [TestCase("powershell")]
    [TestCase("sh")]
    [TestCase("")]
    public void DoesNotContainOtherPrograms(string program) => Assert.That(DotnetAndGit.Contains(program), Is.False);

    [TestCase("./git")]
    [TestCase("tools/git")]
    [TestCase("../dotnet")]
    public void NeverContainsAPathEvenToAListedName(string program) => Assert.That(DotnetAndGit.Contains(program), Is.False);

    [Test]
    public void IgnoresCaseAndTheExeExtensionOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Program names only ignore case on Windows.");

        Assert.That(DotnetAndGit.Contains("Git.EXE"), Is.True);
        Assert.That(DotnetAndGit.Contains("git.cmd"), Is.False);
    }

    [Test]
    public void DescribesItselfForMessages()
    {
        Assert.That(DotnetAndGit.Describe(), Is.EqualTo("dotnet, git"));
        Assert.That(new AllowedPrograms([]).Describe(), Is.EqualTo("none"));
    }

    [Test]
    public void DefaultsToDotnetAndGit() => Assert.That(AllowedPrograms.Defaults, Is.EqualTo(new[] { "dotnet", "git" }));
}
