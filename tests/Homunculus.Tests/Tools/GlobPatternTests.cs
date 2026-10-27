using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class GlobPatternTests
{
    [TestCase("*.cs", "App.cs", true)]
    [TestCase("*.cs", "src/Homunculus/App.cs", true, TestName = "A pattern without a slash matches in any folder")]
    [TestCase("*.cs", "App.csproj", false)]
    [TestCase("src/*.cs", "src/App.cs", true)]
    [TestCase("src/*.cs", "src/Homunculus/App.cs", false, TestName = "A star stays inside one folder")]
    [TestCase("src/**/*.cs", "src/Homunculus/Commands/App.cs", true)]
    [TestCase("src/**/*.cs", "src/App.cs", true, TestName = "Two stars and a slash can match no folder at all")]
    [TestCase("src/**", "src/a/b/c.txt", true)]
    [TestCase("**/Tests/*.cs", "tests/Tests/A.cs", true, TestName = "Case does not matter")]
    [TestCase("**/Tests/*.cs", "x/Other/A.cs", false)]
    [TestCase("Version?.cs", "Version1.cs", true)]
    [TestCase("Version?.cs", "Version12.cs", false)]
    [TestCase("*.{cs,csproj}", "a/App.csproj", true)]
    [TestCase("*.{cs,csproj}", "a/App.txt", false)]
    [TestCase("./src/*.cs", "src/App.cs", true, TestName = "A leading dot slash is ignored")]
    [TestCase("/src/*.cs", "src/App.cs", true, TestName = "A leading slash is ignored")]
    [TestCase("src\\*.cs", "src/App.cs", true, TestName = "Backslashes count as slashes")]
    [TestCase("a+b.cs", "a+b.cs", true, TestName = "Regex characters are taken literally")]
    [TestCase("a+b.cs", "aab.cs", false)]
    [TestCase("(x).cs", "(x).cs", true)]
    public void MatchesPaths(string pattern, string path, bool expected) =>
        Assert.That(new GlobPattern(pattern).IsMatch(path), Is.EqualTo(expected));

    [Test]
    public void ToleratesAnUnclosedBrace() =>
        Assert.That(new GlobPattern("*.{cs").IsMatch("a.cs"), Is.True);
}
