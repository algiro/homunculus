using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Homunculus.Tools;

/// <summary>Runs command lines in the project directory and reports what they printed and how they ended.</summary>
public interface ICommandRunner
{
    /// <exception cref="ToolException">The program could not be found or started, or it ran past the time limit.</exception>
    Task<CommandOutcome> RunAsync(string command, CancellationToken cancellation);
}

public readonly record struct CommandOutcome(string Output, int ExitCode);

/// <summary>
/// Starts one program with its arguments in the project folder. No shell is involved, so nothing in an argument (";", "|",
/// "&amp;&amp;", "$(...)", "&gt;") can start a second program or send output elsewhere. The program is found on PATH, never
/// in the project folder, so a repository cannot slip in its own git or dotnet.
/// </summary>
public sealed class CommandRunner(ProjectDirectory project, TimeSpan timeout, ProgramLocator? locator = null) : ICommandRunner
{
    private readonly ProgramLocator _locator = locator ?? new ProgramLocator(project);

    /// <summary>Runs a command line, such as a check the user configured, split into words by <see cref="CommandLine.Split"/>.</summary>
    public Task<CommandOutcome> RunAsync(string command, CancellationToken cancellation)
    {
        var words = CommandLine.Split(command);
        if (words.Count == 0) throw new ToolException("The command is empty.");
        if (!ProgramName.IsBare(words[0]))
            throw new ToolException($"'{words[0]}' is a path: only programs on PATH are run, never one inside the project.");

        return RunAsync(words[0], words.Skip(1).ToList(), cancellation);
    }

    public async Task<CommandOutcome> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var path = _locator.Find(program)
            ?? throw new ToolException($"'{program}' was not found on PATH. Programs inside the project folder are never run.");

        using var process = Start(path, program, arguments);
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

        return new CommandOutcome(output.ToString(), process.ExitCode);
    }

    private Process Start(string path, string program, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(path)
        {
            WorkingDirectory = project.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            return Process.Start(info)!;
        }
        catch (Win32Exception e)
        {
            throw new ToolException($"Cannot start {program}: {e.Message}");
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }
}

/// <summary>Splitting a command line into words without a shell.</summary>
public static class CommandLine
{
    /// <summary>
    /// Words are separated by whitespace; double quotes group words that contain spaces and are removed. Nothing else is
    /// special: backslashes, pipes, redirects and ";" are ordinary characters handed to the program as they are.
    /// </summary>
    public static IReadOnlyList<string> Split(string line)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        var inWord = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (inWord) words.Add(word.ToString());
                word.Clear();
                inWord = false;
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }

        if (inWord) words.Add(word.ToString());
        return words;
    }
}
