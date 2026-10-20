using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class RunCommandTool(ProjectDirectory project, TimeSpan timeout) : ITool
{
    public string Name => "run_command";
    public string Description => "Run a shell command line in the project directory and return its output and exit code.";
    public JsonObject Parameters => Schema.Object(Parameter.Required("command", "The command line to run."));

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Running;

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        using var process = Start(arguments.Text("command"));
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

    private Process Start(string command)
    {
        var (shell, runFlag) = OperatingSystem.IsWindows() ? ("cmd.exe", "/c") : ("/bin/sh", "-c");
        var info = new ProcessStartInfo(shell)
        {
            WorkingDirectory = project.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(runFlag);
        info.ArgumentList.Add(command);
        return Process.Start(info)!;
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }
}
