using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class RunCommandTool(ICommandRunner runner) : ITool
{
    public RunCommandTool(ProjectDirectory project, TimeSpan timeout, Shell? shell = null)
        : this(new CommandRunner(project, timeout, shell))
    {
    }

    public string Name => "run_command";
    public string Description =>
        $"Run a command line with {runner.Shell.Name} in the project directory ({RuntimeInformation.OSDescription}) " +
        $"and return its output and exit code. Use only syntax that {runner.Shell.Name} understands.";
    public JsonObject Parameters => Schema.Of(Parameter.Required("command", "The command line to run."));

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Running;

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        var (output, exitCode) = await runner.RunAsync(arguments.Text("command"), cancellation);
        return $"{output}\n[exit code {exitCode}]".Truncate(Limits.OutputChars);
    }
}
