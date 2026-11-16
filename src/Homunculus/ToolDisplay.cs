using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>How one tool call ended: whether it failed, a short summary of its result, and how long it ran.</summary>
public sealed record ToolOutcome(bool Failed, string Summary, TimeSpan Elapsed);

/// <summary>
/// Short, readable forms of tool calls and results for the console: the call by what it touches (a path relative to the
/// project, a command line) rather than its raw JSON, and the result by its gist (an error, an exit code, a line count).
/// </summary>
public static partial class ToolDisplay
{
    private const int MaxSummary = 70;

    /// <summary>What the call works on, such as "src/App.cs", "dotnet test" or "3 steps".</summary>
    public static string DescribeCall(ITool tool, JsonElement arguments, string projectRoot)
    {
        ToolEffect effect;
        try
        {
            effect = tool.EffectOf(arguments);
        }
        catch (Exception e) when (e is ToolException or UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return Shorten(arguments.GetRawText());
        }

        var summary = effect switch
        {
            { Command: { } command } => CommandLine(command),
            { Path: { } path } => Relative(path, projectRoot) + EditSize(arguments),
            { Access: ToolAccess.Plan } => Steps(arguments),
            { Access: ToolAccess.Network } => arguments.OptionalText("query") is { } query ? $"\"{query}\"" : arguments.OptionalText("url") ?? arguments.GetRawText(),
            _ => arguments.GetRawText(),
        };
        return Shorten(summary);
    }

    /// <summary>The gist of a result: the error, the exit code of a command, or how many lines came back.</summary>
    public static ToolOutcome DescribeResult(string result, string projectRoot, TimeSpan elapsed, bool refused = false)
    {
        if (refused || result.StartsWith("Error:", StringComparison.Ordinal))
            return new(true, Shorten(FirstLine(result)), elapsed);

        if (ExitCode().Match(result) is { Success: true } exit)
        {
            var code = int.Parse(exit.Groups[1].Value, CultureInfo.InvariantCulture);
            var output = Lines(result[..exit.Index]);
            return new(code != 0, code == 0 ? $"exit 0 · {output}" : $"exit {code} · {output}", elapsed);
        }

        var text = result.TrimEnd();
        if (text.Length == 0) return new(false, "no output", elapsed);
        if (!text.Contains('\n')) return new(false, Shorten(text.Replace(projectRoot + Path.DirectorySeparatorChar, "", StringComparison.Ordinal)), elapsed);
        return new(false, Lines(text), elapsed);
    }

    /// <summary>The time a tool took, worth showing only when it was long enough to notice.</summary>
    public static string? Duration(TimeSpan elapsed) =>
        elapsed >= TimeSpan.FromSeconds(0.5) ? UsageText.Describe(elapsed) : null;

    [GeneratedRegex(@"\n?\[exit code (-?\d+)\]\s*$")]
    private static partial Regex ExitCode();

    private static string CommandLine(string command)
    {
        var parts = JsonSerializer.Deserialize<string[]>(command) ?? [];
        return string.Join(' ', parts.Select(part => part.Length == 0 || part.Any(char.IsWhiteSpace) ? $"\"{part}\"" : part));
    }

    private static string Relative(string path, string projectRoot)
    {
        var relative = Path.GetRelativePath(projectRoot, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }

    /// <summary>For an edit, how many lines go in and out: "+4 −2".</summary>
    private static string EditSize(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("new_str", out var added) || added.ValueKind != JsonValueKind.String)
            return "";

        var removed = arguments.TryGetProperty("old_str", out var old) && old.ValueKind == JsonValueKind.String ? old.GetString()! : "";
        return $"  +{LineCount(added.GetString()!)} −{LineCount(removed)}";
    }

    private static string Steps(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array
            ? Count(steps.GetArrayLength(), "step")
            : "";

    private static int LineCount(string text) => text.Length == 0 ? 0 : text.TrimEnd('\n').Split('\n').Length;

    private static string Lines(string text) => text.Trim().Length == 0 ? "no output" : Count(LineCount(text.Trim()), "line");

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string FirstLine(string text) => text.ReplaceLineEndings("\n").Split('\n')[0].Trim();

    private static string Shorten(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        return line.Length <= MaxSummary ? line : line[..(MaxSummary - 1)] + "…";
    }
}
