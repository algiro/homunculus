using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>An error line from a check. <see cref="Key"/> is the line without its position, so the same error is the same error after lines above it move.</summary>
public sealed record CheckError(string Key, string Line);

/// <summary>What a check said: whether it passed, the text the model should read about it, and the errors it found.</summary>
public sealed record CheckResult(bool Passed, string Report)
{
    public IReadOnlyList<CheckError> Errors { get; init; } = [];

    /// <summary>True when the command could not run to the end (it timed out, say), so its silence proves nothing.</summary>
    public bool DidNotFinish { get; init; }
}

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

    [GeneratedRegex(@"\(\d+(?:,\s*\d+)*\)(?=:)|:\d+(?::\d+)?(?=:)")]
    private static partial Regex Position();

    [GeneratedRegex(@"^\s*(?:\d+|no)\s+errors?|^\s*Build FAILED", RegexOptions.IgnoreCase)]
    private static partial Regex Summary();

    public async Task<CheckResult> RunAsync(CancellationToken cancellation)
    {
        try
        {
            var (output, exitCode) = await runner.RunAsync(command, cancellation);
            if (exitCode == 0) return new CheckResult(true, "Check passed.");

            var lines = Lines(output);
            var errors = lines.Where(IsError).Select(line => new CheckError(Position().Replace(line, ""), line)).ToList();
            return new CheckResult(false, $"Check `{command}` failed (exit code {exitCode}):\n{Summarize(lines, errors)}") { Errors = errors };
        }
        catch (ToolException e)
        {
            return new CheckResult(false, $"Check `{command}` did not finish: {e.Message}") { DidNotFinish = true };
        }
    }

    /// <summary>The distinct non-empty lines of the output, with the project file glued to the end of a build's lines and the project's path removed.</summary>
    private List<string> Lines(string output)
    {
        var rootPrefix = project is null ? null : project.Root + Path.DirectorySeparatorChar;
        return output.Split('\n')
            .Select(line => ProjectSuffix().Replace(line.TrimEnd(), ""))
            .Select(line => rootPrefix is null ? line : line.Replace(rootPrefix, "", StringComparison.OrdinalIgnoreCase))
            .Where(line => line.Length > 0)
            .Distinct()
            .ToList();
    }

    /// <summary>A line that reports an error; a build's totals ("2 Error(s)") are not one, or fixing one of two errors would look like a new error.</summary>
    private static bool IsError(string line) => line.Contains("error", StringComparison.OrdinalIgnoreCase) && !Summary().IsMatch(line);

    /// <summary>The lines worth reading: those that mention an error, or the last lines when none does.</summary>
    private static string Summarize(List<string> lines, List<CheckError> errors)
    {
        var chosen = errors.Count > 0 ? errors.Select(error => error.Line).ToList() : lines.TakeLast(MaxLines).ToList();

        var text = string.Join('\n', chosen.Take(MaxLines));
        if (chosen.Count > MaxLines) text += $"\n[{chosen.Count - MaxLines} more lines]";
        return text.Length <= MaxChars ? text : text[..MaxChars] + "...";
    }
}
