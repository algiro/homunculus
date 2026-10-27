using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

/// <summary>
/// Starts one program with a list of arguments, in the project folder. No shell is involved, so nothing in an argument (";",
/// "|", "&amp;&amp;", "$(...)", "&gt;") can start a second program or send output elsewhere. The program is found on PATH,
/// never in the project folder. Which programs may run without asking is decided by the approvals, not here.
/// </summary>
public sealed class RunCommandTool(ProjectDirectory project, TimeSpan timeout, AllowedPrograms? allowed = null, ProgramLocator? locator = null) : ITool
{
private readonly Shell _shell = shell ?? Shell.Detect();

public string Name => "run_command";
public string Description =>
    $"Run a command line with {_shell.Name} in the project directory ({RuntimeInformation.OSDescription}) " +
    $"and return its output and exit code. Use only syntax that {_shell.Name} understands.";
public JsonObject Parameters => Schema.Of(Parameter.Required("command", "The command line to run."));

    private readonly AllowedPrograms _allowed = allowed ?? new AllowedPrograms(AllowedPrograms.Defaults);
    private readonly ProgramLocator _locator = locator ?? new ProgramLocator(project);

    public string Name => ToolName;

    public string Description =>
        "Run one program in the project directory and return its output and exit code. Give the program and its arguments " +
        """separately, for example {"program":"dotnet","args":["test"]}. No shell is involved: pipes, redirects, &&, ;, wildcards """ +
        "and shell built-ins (cd, dir, type, echo) do not work. Use it to build, test or run git, never to read, list, find or " +
        "change files: list_files, read_file and edit_file do that. " +
        $"Allowed programs: {_allowed.Describe()}. Any other program needs the user's approval every time.";

    public JsonObject Parameters => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["program"] = new JsonObject { ["type"] = "string", ["description"] = "The program's name only, such as 'dotnet' or 'git'." },
            ["args"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "The arguments, one item each, such as [\"test\", \"--no-build\"].",
            },
        },
        ["required"] = new JsonArray("program"),
    };

    /// <exception cref="ToolException">The arguments do not name one program and a list of arguments.</exception>
    public ToolEffect EffectOf(JsonElement arguments)
    {
        var (program, args) = Parse(arguments);
        return ToolEffect.Running(program, args);
    }

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        var (program, args) = Parse(arguments);
        var path = _locator.Find(program)
            ?? throw new ToolException($"'{program}' was not found on PATH. Programs inside the project folder are never run.");

        using var process = Start(path, args);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(timeout);

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            cancellation.ThrowIfCancellationRequested();
            throw new ToolException($"Command timed out after {timeout.TotalSeconds:0} seconds.");
        }

        return $"{output}\n[exit code {process.ExitCode}]".Truncate(Limits.OutputChars);
    }

    /// <summary>The program and its arguments, with an explanation the model can act on when they are not given that way.</summary>
    private static (string Program, IReadOnlyList<string> Arguments) Parse(JsonElement arguments)
    {
var program = arguments.OptionalText("program")?.Trim();
if (string.IsNullOrEmpty(program))
{
    // fallback or error handling here if needed
}

var info = new ProcessStartInfo(_shell.Executable);
        {
            if (arguments.OptionalText("command") is not null)
                throw new ToolException(
                    """run_command takes a program and a list of arguments, not a command line: for example {"program":"dotnet","args":["test"]}. """ +
                    "No shell is involved, so pipes, redirects, && and ; do not work.");
            throw new ToolException("Missing argument 'program'.");
        }

        var args = OptionalTextList(arguments, "args");
        if (program.Any(char.IsWhiteSpace))
        {
            var words = program.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            throw new ToolException(
                $"'{program}' is not a program's name. Give the program alone and each argument as an item of args, for example " +
                $"{JsonSerializer.Serialize(new { program = words[0], args = words.Skip(1).Concat(args) })}.");
        }

        if (!ProgramName.IsBare(program))
            throw new ToolException($"Give the program's name only, such as 'dotnet', not a path ('{program}'): programs are looked up on PATH.");

        return (program, args);
    }

    private static List<string> OptionalTextList(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null)
            return [];
        if (list.ValueKind != JsonValueKind.Array)
            throw new ToolException($"'{name}' must be a list of texts, one item per argument, such as [\"test\", \"--no-build\"].");

        return list.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new ToolException($"Every item in '{name}' must be text."))
            .ToList();
    }

    private Process Start(string path, IReadOnlyList<string> args)
    {
        var info = new ProcessStartInfo(path)
        {
            WorkingDirectory = project.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
foreach (var argument in args) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }
}
