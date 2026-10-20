using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>Runs one tool call the model asked for, if approved, and turns any failure into text the model can read.</summary>
public sealed class ToolRunner(ToolRegistry tools, Approvals approvals, IUserInterface ui)
{
    public async Task<string> RunAsync(ToolCall call, CancellationToken cancellation)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
            return $"Error: unknown tool '{call.Name}'.";

        ui.Show($"  -> {call.Name} {call.Arguments.GetRawText()}");
        try
        {
            var refusal = await approvals.CheckAsync(tool, call.Arguments, cancellation);
            return refusal ?? await tool.RunAsync(call.Arguments, cancellation);
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            return $"Error: {e.Message}";
        }
    }
}
