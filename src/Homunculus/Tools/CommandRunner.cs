using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Homunculus.Tools;

/// <summary>Runs command lines with a shell in the project directory and reports what they printed and how they ended.</summary>
public interface ICommandRunner
{
    Shell Shell { get; }

    /// <exception cref="ToolException">The shell could not be started, or the command ran past the time limit.</exception>
    Task<CommandOutcome> RunAsync(string command, CancellationToken cancellation);
}

public readonly record struct CommandOutcome(string Output, int ExitCode);

public sealed class CommandRunner(ProjectDirectory project, TimeSpan timeout, Shell? shell = null) : ICommandRunner
{
    public Shell Shell { get; } = shell ?? Shell.Detect();

    public async Task<CommandOutcome> RunAsync(string command, CancellationToken cancellation)
    {
        using var process = Start(command);
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

    private Process Start(string command)
    {
        var info = new ProcessStartInfo(Shell.Executable)
        {
            WorkingDirectory = project.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in Shell.Arguments) info.ArgumentList.Add(argument);
        info.ArgumentList.Add(command);
        try
        {
            return Process.Start(info)!;
        }
        catch (Win32Exception e)
        {
            throw new ToolException($"Cannot start {Shell.Name}: {e.Message}");
        }
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }
}
