using System.Runtime.InteropServices;
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
private readonly CommandRunner _runner;
private readonly ProgramLocator _locator;

public RunCommandTool(ProjectDirectory project, TimeSpan timeout, AllowedPrograms? allowed = null, ProgramLocator? locator = null)
{
    _locator = locator ?? new ProgramLocator(project);
    _runner = new CommandRunner(project, timeout, null);
}

public string Name => "run_command";
public string Description =>
    $"Run a command line with {_shell.Name} in the project directory ({RuntimeInformation.OSDescription}) " +
    $"and return its output and exit code. Use only syntax that {_shell.Name} understands.";
public JsonObject Parameters => Schema.Of(Parameter.Required("command", "The command line to run."));

    private readonly AllowedPrograms _allowed = allowed ?? new AllowedPrograms(AllowedPrograms.Defaults);
    private readonly ProgramLocator _locator = locator ?? new ProgramLocator(project);

    public string Name => ToolName;

    public string Description =>
    $"Run a command line with {_runner.Shell.Name} in the project directory ({RuntimeInformation.OSDescription}) " +
    $"and return its output and exit code. Use only syntax that {_runner.Shell.Name} understands. " +
    "Do not use it to create or edit files: write_file, edit_file and insert_lines do that, and their changes are checked and tracked.";
public JsonObject Parameters => Schema.Of(Parameter.Required("command", "The command line to run."));

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
var (output, exitCode) = await _runner.RunAsync(arguments.Text("command"), cancellation);
return $"{output}\n[exit code {exitCode}]".TruncateKeepingEnd(Limits.CommandOutputChars);
    }
/// <summary>The program and its arguments, with an explanation the model can act on when they are not given that way.</summary>
private static (string Program, IReadOnlyList<string> Arguments) Parse(JsonElement arguments)
{
    var program = arguments.OptionalText("program")?.Trim();
    if (string.IsNullOrEmpty(program))
    {
        if (arguments.OptionalText("command") is not null)
            throw new ToolException(
                "run_command takes a program and a list of arguments, not a command line: for example {\"program\":\"dotnet\",\"args\":[\"test\"]}. " +
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
        return new List<string>();
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
    foreach (var argument in _shell.Arguments) info.ArgumentList.Add(argument);
    foreach (var arg in args) info.ArgumentList.Add(arg);
    try
    {
        return Process.Start(info)!;
    }
    catch (Win32Exception e)
    {
        throw new ToolException($"Cannot start {_shell.Name}: {e.Message}");
    }
}

private static void Append(StringBuilder output, string? line)
{
    if (line is null) return;
    lock (output) output.AppendLine(line);
}
}
