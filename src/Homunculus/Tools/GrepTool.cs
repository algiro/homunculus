using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Homunculus.Tools;

public sealed class GrepTool(ProjectDirectory project) : FileTool(project)
{
    public const string ToolName = "grep";

    private const int MaxFileBytes = 1_000_000;
    private const int MaxLineChars = 200;

    private static readonly HashSet<string> LogExtensions = new(StringComparer.OrdinalIgnoreCase) { ".log", ".jsonl" };

    public override string Name => ToolName;
    public override string Description =>
        $"Search the text of files with a regular expression; each result is 'file:line: text' (at most {Limits.SearchResults}). " +
        "Use it to find where something is defined or used before guessing a path. Skips bin, obj, .git, logs and binary files.";
    public override JsonObject Parameters => Schema.Of(
        Parameter.Required("pattern", "Regular expression to look for, such as 'class \\w+Command' or 'ExitCommand'."),
        Parameter.Optional("path", "Folder or file to search; defaults to the project root."),
        Parameter.Optional("include", "Only search files matching this name pattern, such as '*.cs'."));
    protected override ToolAccess Access => ToolAccess.Read;

    protected override string PathOf(JsonElement arguments) =>
        Project.Resolve(arguments.OptionalText("path") ?? ".");

    protected override string Run(JsonElement arguments, string path)
    {
        var regex = Compile(arguments.Text("pattern"));
        var include = arguments.OptionalText("include") is { Length: > 0 } text ? new GlobPattern(text) : null;

        var root = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        IEnumerable<string> files = Directory.Exists(path) ? SearchableFiles.Under(path)
            : File.Exists(path) ? new[] { path }
            : throw new ToolException($"Not found: {path}");

        var results = new List<string>();
        foreach (var file in files.Where(file => include is null || include.IsMatch(Path.GetRelativePath(root, file).Replace('\\', '/'))))
        {
            if (!IsLog(file, path) && IsSearchable(file) && !AddMatches(results, file, regex)) break;
        }

        if (results.Count == 0) return "No matches.";
        return results.Count > Limits.SearchResults
            ? string.Join('\n', results.Take(Limits.SearchResults)) + "\n[more matches not shown; use a narrower pattern, path or include]"
            : string.Join('\n', results);
    }

    /// <summary>
    /// Logs and transcripts repeat every name the model ever saw and would drown the real hits. A file named as the search
    /// path itself is always searched.
    /// </summary>
    private static bool IsLog(string file, string searchPath) =>
        file != searchPath
        && (LogExtensions.Contains(Path.GetExtension(file))
            || Path.GetRelativePath(searchPath, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .SkipLast(1).Contains("logs", StringComparer.OrdinalIgnoreCase));

    private static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException e)
        {
            throw new ToolException($"Invalid regular expression: {e.Message}");
        }
    }

    private static bool IsSearchable(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > MaxFileBytes) return false;

            using var stream = File.OpenRead(file);
            var head = new byte[Math.Min(8000, (int)info.Length)];
            var read = stream.Read(head, 0, head.Length);
            return !head.AsSpan(0, read).Contains((byte)0);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Adds the file's matching lines; false once there are enough results to stop looking.</summary>
    private bool AddMatches(List<string> results, string file, Regex regex)
    {
        var shown = Relative(file);
        var number = 0;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                number++;
                if (!regex.IsMatch(line)) continue;

                results.Add($"{shown}:{number}: {Shorten(line.Trim())}");
                if (results.Count > Limits.SearchResults) return false;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ToolException("The regular expression took too long; use a simpler one.");
        }
        catch (IOException)
        {
            // A file that is locked or vanished while searching is just left out.
        }

        return true;
    }

    private static string Shorten(string line) => line.Length <= MaxLineChars ? line : line[..MaxLineChars] + "...";
}
