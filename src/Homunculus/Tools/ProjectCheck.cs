using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>What a check said: whether it passed, and the text the model should read about it.</summary>
public sealed record CheckResult(bool Passed, string Report);

/// <summary>
/// A command the user chose to prove the project still works (a build, a linter, quick tests). It runs after every change
/// to a file, so a model that wrote something wrong hears about it at once instead of after a dozen more edits.
/// </summary>
public sealed partial class ProjectCheck(string command, ICommandRunner runner, ProjectDirectory? project = null)
{
    private const int MaxLines = 15;
    private const int MaxChars = 3000;

    [GeneratedRegex(@"\s*\[[^\]]*\.(?:csproj|vbproj|fsproj|proj|sln|slnx)\]\s*$")]
    private static partial Regex ProjectSuffix();

    public async Task<CheckResult> RunAsync(CancellationToken cancellation)
    {
        try
        {
            var (output, exitCode) = await runner.RunAsync(command, cancellation);
            return exitCode == 0
                ? new CheckResult(true, "Check passed.")
                : new CheckResult(false, $"Check `{command}` failed (exit code {exitCode}):\n{Summarize(output)}");
        }
        catch (ToolException e)
        {
            return new CheckResult(false, $"Check `{command}` did not finish: {e.Message}");
        }
    }

    /// <summary>
    /// The lines worth reading: those that mention an error (a build prints each one more than once, with the project file
    /// glued to the end), or the last lines when none does.
    /// </summary>
    private string Summarize(string output)
    {
        var rootPrefix = project is null ? null : project.Root + Path.DirectorySeparatorChar;
        var lines = output.Split('\n')
            .Select(line => ProjectSuffix().Replace(line.TrimEnd(), ""))
            .Select(line => rootPrefix is null ? line : line.Replace(rootPrefix, "", StringComparison.OrdinalIgnoreCase))
            .Where(line => line.Length > 0)
            .Distinct()
            .ToList();
        var errors = lines.Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).ToList();
        var chosen = errors.Count > 0 ? errors : lines.TakeLast(MaxLines).ToList();

        var text = string.Join('\n', chosen.Take(MaxLines));
        if (chosen.Count > MaxLines) text += $"\n[{chosen.Count - MaxLines} more lines]";
        return text.Length <= MaxChars ? text : text[..MaxChars] + "...";
    }
}
