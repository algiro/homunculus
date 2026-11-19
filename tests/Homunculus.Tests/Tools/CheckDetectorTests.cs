using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class CheckDetectorTests
{
    private const string Temporary = "TEMP";

    private TempProject _project = null!;

    [SetUp] public void SetUp() => _project = new TempProject();

    [TearDown]
    public void TearDown()
    {
        _project?.Dispose();
        _project = null!;
    }

    private void NodeModules(bool withTypeScript)
    {
        _project.Write("node_modules/.bin/eslint", "");
        if (withTypeScript) _project.Write("node_modules/.bin/tsc", "");
    }

    private DetectedCheck? Detect(bool windows, params string[] installed) =>
        new CheckDetector(installed.Contains, windows, Temporary).Detect(_project.Root);

    private DetectedCheck? DetectEverythingInstalled(bool windows = false) =>
        new CheckDetector(_ => true, windows, Temporary).Detect(_project.Root);

    [Test]
    public void FindsNothingInAnEmptyFolder() => Assert.That(DetectEverythingInstalled(), Is.Null);

    [Test]
    public void FindsNothingFromMarkersInSubfolders()
    {
        _project.Write("src/go.mod", "module x");
        _project.Write("sub/App.sln", "");

        Assert.That(DetectEverythingInstalled(), Is.Null);
    }

    [Test]
    public void BuildsADotNetSolution()
    {
        _project.Write("App.sln", "");

        Assert.That(Detect(false, "dotnet"), Is.EqualTo(new DetectedCheck(".NET", "dotnet build \"App.sln\" -v q --nologo")));
    }

    [Test]
    public void BuildsASlnxSolution()
    {
        _project.Write("Homunculus.slnx", "");

        Assert.That(Detect(false, "dotnet")!.Command, Is.EqualTo("dotnet build \"Homunculus.slnx\" -v q --nologo"));
    }

    [Test]
    public void BuildsALoneProjectFile()
    {
        _project.Write("Tool.fsproj", "");

        Assert.That(Detect(false, "dotnet")!.Command, Is.EqualTo("dotnet build \"Tool.fsproj\" -v q --nologo"));
    }

    [Test]
    public void PrefersASolutionToAProject()
    {
        _project.Write("App.csproj", "");
        _project.Write("Z.sln", "");

        Assert.That(Detect(false, "dotnet")!.Command, Does.Contain("Z.sln"));
    }

    [Test]
    public void NeedsDotNetToBeInstalled()
    {
        _project.Write("App.sln", "");

        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void BuildsAGoModule()
    {
        _project.Write("go.mod", "module x");

        Assert.That(Detect(false, "go"), Is.EqualTo(new DetectedCheck("Go", "go build ./...")));
        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void ChecksARustCrateWithShortMessages()
    {
        _project.Write("Cargo.toml", "[package]");

        Assert.That(Detect(false, "cargo"), Is.EqualTo(new DetectedCheck("Rust", "cargo check --quiet --message-format short")));
        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void TypeChecksATypeScriptProjectWithoutDownloadingTheCompiler()
    {
        _project.Write("tsconfig.json", "{}");
        NodeModules(withTypeScript: true);

        Assert.That(Detect(false, "npx"), Is.EqualTo(new DetectedCheck("TypeScript", "npx --no-install tsc --noEmit")));
        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void DoesNotTypeCheckBeforeTheDependenciesAreInstalled()
    {
        _project.Write("tsconfig.json", "{}");

        Assert.That(Detect(false, "npx", "npm"), Is.Null, "no node_modules: every run would fail");
    }

    [Test]
    public void DoesNotTypeCheckWithoutALocalCompiler()
    {
        _project.Write("tsconfig.json", "{}");
        NodeModules(withTypeScript: false);

        Assert.That(Detect(false, "npx", "npm"), Is.Null);
    }

    [Test]
    public void DoesNotRunANodeScriptBeforeTheDependenciesAreInstalled()
    {
        _project.Write("package.json", "{\"scripts\":{\"typecheck\":\"tsc\"}}");

        Assert.That(Detect(false, "npm"), Is.Null);
    }

    [TestCase("typecheck")]
    [TestCase("type-check")]
    [TestCase("check")]
    public void RunsANodeProjectsOwnTypeCheckScript(string script)
    {
        _project.Write("package.json", "{\"scripts\":{\"build\":\"x\",\"" + script + "\":\"tsc\"}}");
        NodeModules(withTypeScript: false);

        Assert.That(Detect(false, "npm"), Is.EqualTo(new DetectedCheck("Node", $"npm run {script} --silent")));
    }

    [Test]
    public void FindsNothingForANodeProjectWithoutSuchAScript()
    {
        _project.Write("package.json", """{"scripts":{"build":"webpack","test":"jest"}}""");
        NodeModules(withTypeScript: false);

        Assert.That(Detect(false, "npm", "npx"), Is.Null);
    }

    [TestCase("not json")]
    [TestCase("""{"scripts":"typecheck"}""")]
    [TestCase("""{"name":"x"}""")]
    public void IgnoresAPackageJsonItCannotUse(string content)
    {
        _project.Write("package.json", content);
        NodeModules(withTypeScript: false);

        Assert.That(Detect(false, "npm"), Is.Null);
    }

    [Test]
    public void SyntaxChecksAPythonProjectWithBytecodeKeptOutOfIt()
    {
        _project.Write("requirements.txt", "requests");

        var found = Detect(false, "python3")!;

        Assert.That(found.Language, Is.EqualTo("Python"));
        Assert.That(found.Command, Is.EqualTo(
            $"python3 -X \"pycache_prefix={Path.Combine(Temporary, "homunculus-pycache")}\" -m compileall -q -x \"(\\.venv|venv|node_modules|\\.git)\" ."));
    }

    [Test]
    public void FindsAPythonProjectFromAPythonFileAlone()
    {
        _project.Write("main.py", "print()");

        Assert.That(Detect(false, "python")!.Command, Does.StartWith("python -X"));
    }

    [TestCase(true, new[] { "python", "python3" }, "python -X")]
    [TestCase(true, new[] { "python3" }, "python3 -X")]
    [TestCase(false, new[] { "python", "python3" }, "python3 -X")]
    [TestCase(false, new[] { "python" }, "python -X")]
    public void PicksTheRightPythonForTheSystem(bool windows, string[] installed, string start)
    {
        _project.Write("pyproject.toml", "");

        Assert.That(Detect(windows, installed)!.Command, Does.StartWith(start));
    }

    [Test]
    public void NeedsPythonToBeInstalled()
    {
        _project.Write("pyproject.toml", "");

        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void CompilesAMavenProject()
    {
        _project.Write("pom.xml", "<project/>");

        Assert.That(Detect(false, "mvn"), Is.EqualTo(new DetectedCheck("Maven", "mvn -q -B compile")));
        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void NeverUsesTheProjectsOwnGradleWrapper()
    {
        _project.Write("build.gradle.kts", "");
        _project.Write("gradlew", "");
        _project.Write("gradlew.bat", "");

        Assert.That(Detect(true, "gradle")!.Command, Is.EqualTo("gradle -q classes"));
        Assert.That(Detect(false, "gradle")!.Command, Is.EqualTo("gradle -q classes"));
        Assert.That(Detect(true), Is.Null, "without an installed gradle there is no check");
    }

    [Test]
    public void FallsBackToAnInstalledGradle()
    {
        _project.Write("settings.gradle", "");

        Assert.That(Detect(false, "gradle")!.Command, Is.EqualTo("gradle -q classes"));
        Assert.That(Detect(false), Is.Null);
    }

    [Test]
    public void PrefersDotNetToEverythingElse()
    {
        _project.Write("App.sln", "");
        _project.Write("go.mod", "");
        _project.Write("Cargo.toml", "");

        Assert.That(DetectEverythingInstalled()!.Language, Is.EqualTo(".NET"));
    }

    [Test]
    public void SkipsALanguageWhoseToolIsMissingAndTriesTheNext()
    {
        _project.Write("App.sln", "");
        _project.Write("go.mod", "");

        Assert.That(Detect(false, "go")!.Language, Is.EqualTo("Go"));
    }

    [Test]
    public void KeepsTheDocumentedOrder()
    {
        _project.Write("go.mod", "");
        _project.Write("Cargo.toml", "");
        _project.Write("tsconfig.json", "");
        NodeModules(withTypeScript: true);
        _project.Write("main.py", "");
        _project.Write("pom.xml", "");
        _project.Write("build.gradle", "");

        var order = new List<string>();
        foreach (var removed in new[] { "go.mod", "Cargo.toml", "tsconfig.json", "main.py", "pom.xml" })
        {
            order.Add(DetectEverythingInstalled()!.Language);
            File.Delete(_project.PathOf(removed));
        }

        order.Add(DetectEverythingInstalled()!.Language);

        Assert.That(order, Is.EqualTo(new[] { "Go", "Rust", "TypeScript", "Python", "Maven", "Gradle" }));
    }
}
