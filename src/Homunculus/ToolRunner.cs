using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Runs one tool call the model asked for, if approved. Failures the model can act on (a tool's own complaint, a missing file,
/// a locked or forbidden path) become text it can read. Anything else is a bug in a tool, so it is not hidden and propagates.
/// </summary>
public sealed class ToolRunner(ToolRegistry tools, Approvals approvals, IUserInterface ui, IEventLog? log = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;

    public async Task<string> RunAsync(ToolCall call, CancellationToken cancellation)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
        {
            _log.Write("tool_unknown", new { call.Id, call.Name });
            return ShowFailure($"unknown tool '{call.Name}'.");
        }

        ui.Show($"  -> {call.Name} {call.Arguments.GetRawText()}");
        _log.Write("tool_call", new { call.Id, call.Name, arguments = call.Arguments });
        string result;
        try
        {
            var refusal = await approvals.CheckAsync(tool, call.Arguments, cancellation);
            result = refusal ?? await tool.RunAsync(call.Arguments, cancellation);
            if (refusal is null && tool.EffectOf(call.Arguments).Access == ToolAccess.Plan && result.Length > 0)
                ui.Show(result);
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            result = ShowFailure(e.Message);
        }

        _log.Write("tool_result", new { call.Id, call.Name, result = JsonlEventLog.Truncate(result) });
        return result;
    }

    /// <summary>The model reads the failure as the tool's result; the user sees it too, so a later claim of success can be checked.</summary>
    private string ShowFailure(string message)
    {
        var failure = $"Error: {message}";
        ui.Show($"  <- {failure}");
        return failure;
    }
}
