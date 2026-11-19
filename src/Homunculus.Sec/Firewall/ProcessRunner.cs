using System.Diagnostics;
using System.Text;

namespace Homunculus.Sec.Firewall;

public readonly record struct ProcessResult(int ExitCode, string Output);

/// <summary>Starts one program with a list of arguments. No shell is involved, so no argument can start anything else.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation);
}

public sealed class ProcessRunner(TimeSpan? timeout = null) : IProcessRunner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(20);

    public async Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new FirewallException($"Cannot start {file}: {e.Message}");
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(_timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new FirewallException($"{file} did not finish within {_timeout.TotalSeconds:0} seconds.");
        }

        lock (output) return new ProcessResult(process.ExitCode, output.ToString().Trim());
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }
}
