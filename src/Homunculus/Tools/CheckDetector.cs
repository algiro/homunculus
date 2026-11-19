using System.Text.Json;

namespace Homunculus.Tools;

/// <summary>A check command found for a project, and what it was found from.</summary>
public sealed record DetectedCheck(string Language, string Command);

/// <summary>
/// Chooses a command that proves a project still compiles, from the files in its root, so that the user does not have to.
/// It only suggests a command whose program is installed. The commands are the quick kind (a build or a type check, not the
/// tests), chosen to print errors a model can read, and to leave no files behind.
/// </summary>
public sealed class CheckDetector(Func<string, bool> isOnPath, bool windows, string temporaryFolder)
{
    private static readonly string[] TypeCheckScripts = ["typecheck", "type-check", "check"];
    private static readonly string[] PythonMarkers = ["pyproject.toml", "setup.py", "setup.cfg", "requirements.txt", "Pipfile"];
    private static readonly string[] WindowsPythons = ["python", "python3"];
    private static readonly string[] OtherPythons = ["python3", "python"];

    /// <summary>Suggests only programs the check can start: found on PATH outside the project, the way run_command finds them.</summary>
    public static CheckDetector ForThisMachine(ProjectDirectory project)
    {
        var locator = new ProgramLocator(project);
        return new(name => locator.Find(name) is not null, OperatingSystem.IsWindows(), Path.GetTempPath());
    }

    /// <summary>The first match in the order .NET, Go, Rust, TypeScript, Python, Maven, Gradle; null when nothing is recognised.</summary>
    public DetectedCheck? Detect(string root)
    {
        var files = Directory.EnumerateFiles(root).Select(file => Path.GetFileName(file)).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return DotNet(files) ?? Go(files) ?? Rust(files) ?? TypeScript(root, files) ?? Python(files) ?? Maven(files) ?? Gradle(files);
    }

    private DetectedCheck? DotNet(List<string> files)
    {
        var target = files.FirstOrDefault(file => HasExtension(file, ".slnx", ".sln"))
                     ?? files.FirstOrDefault(file => HasExtension(file, ".csproj", ".fsproj", ".vbproj"));
        return target is not null && isOnPath("dotnet") ? new DetectedCheck(".NET", $"dotnet build \"{target}\" -v q --nologo") : null;
    }

    private DetectedCheck? Go(List<string> files) =>
        files.Contains("go.mod") && isOnPath("go") ? new DetectedCheck("Go", "go build ./...") : null;

    private DetectedCheck? Rust(List<string> files) =>
        files.Contains("Cargo.toml") && isOnPath("cargo") ? new DetectedCheck("Rust", "cargo check --quiet --message-format short") : null;

    /// <summary>A TypeScript project is type-checked with the compiler it already has; npx must not download one.</summary>
    private DetectedCheck? TypeScript(string root, List<string> files)
    {
        // Without installed dependencies every run would fail for a reason that has nothing to do with the model's edit.
        if (!Directory.Exists(Path.Combine(root, "node_modules"))) return null;

        if (files.Contains("tsconfig.json") && isOnPath("npx") && HasLocalCompiler(root))
            return new DetectedCheck("TypeScript", "npx --no-install tsc --noEmit");

        var script = files.Contains("package.json") && isOnPath("npm") ? TypeCheckScript(Path.Combine(root, "package.json")) : null;
        return script is null ? null : new DetectedCheck("Node", $"npm run {script} --silent");
    }

    private static bool HasLocalCompiler(string root) =>
        File.Exists(Path.Combine(root, "node_modules", ".bin", "tsc")) || File.Exists(Path.Combine(root, "node_modules", ".bin", "tsc.cmd"));

    private static string? TypeCheckScript(string packageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            if (!document.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object) return null;

            return TypeCheckScripts.FirstOrDefault(name => scripts.TryGetProperty(name, out _));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Python has no build, so the check is a syntax check of every file; bytecode goes to a temporary folder, not the project.</summary>
    private DetectedCheck? Python(List<string> files)
    {
        if (!files.Any(file => PythonMarkers.Contains(file) || HasExtension(file, ".py"))) return null;

        var python = (windows ? WindowsPythons : OtherPythons).FirstOrDefault(isOnPath);
        if (python is null) return null;

        var bytecode = Path.Combine(temporaryFolder, "homunculus-pycache");
        return new DetectedCheck("Python", $"{python} -X \"pycache_prefix={bytecode}\" -m compileall -q -x \"(\\.venv|venv|node_modules|\\.git)\" .");
    }

    private DetectedCheck? Maven(List<string> files) =>
        files.Contains("pom.xml") && isOnPath("mvn") ? new DetectedCheck("Maven", "mvn -q -B compile") : null;

    private DetectedCheck? Gradle(List<string> files)
    {
        if (!files.Any(file => file.StartsWith("build.gradle", StringComparison.Ordinal) || file.StartsWith("settings.gradle", StringComparison.Ordinal)))
            return null;

        // Never the project's own gradlew wrapper: a file the repository controls is not run after every edit.
        return isOnPath("gradle") ? new DetectedCheck("Gradle", "gradle -q classes") : null;
    }

    private static bool HasExtension(string file, params string[] extensions) =>
        extensions.Any(extension => file.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
