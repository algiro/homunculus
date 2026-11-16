using System.Diagnostics;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Runs one tool call the model asked for, if approved. Failures the model can act on (a tool's own complaint, a missing file,
/// a locked or forbidden path) become text it can read. Anything else is a bug in a tool, so it is not hidden and propagates.
/// </summary>
public sealed class ToolRunner(ToolRegistry tools, Approvals approvals, IUserInterface ui, IEventLog? log = null, string? root = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;

    /// <summary>Paths are shown relative to this folder, the project's root.</summary>
    private readonly string _projectRoot = root ?? Directory.GetCurrentDirectory();

    public async Task<string> RunAsync(ToolCall call, CancellationToken cancellation)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
        {
            _log.Write("tool_unknown", new { call.Id, call.Name });
            return $"Error: unknown tool '{call.Name}'.";
        }

        ui.ShowToolCall(call.Name, ToolDisplay.DescribeCall(tool, call.Arguments, _projectRoot));
        _log.Write("tool_call", new { call.Id, call.Name, arguments = call.Arguments });
        string result;
        string? refusal = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            // The approval may ask the user, so the progress shows only once the tool itself runs.
            refusal = await approvals.CheckAsync(tool, call.Arguments, cancellation);
            if (refusal is null)
            {
                started = Stopwatch.GetTimestamp();
                using (ui.ShowProgress("running"))
                    result = await tool.RunAsync(call.Arguments, cancellation);
            }
            else
            {
                result = refusal;
            }
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            result = $"Error: {e.Message}";
        }

        ui.ShowToolResult(ToolDisplay.DescribeResult(result, _projectRoot, Stopwatch.GetElapsedTime(started), refused: refusal is not null));
        _log.Write("tool_result", new { call.Id, call.Name, result = JsonlEventLog.Truncate(result) });
        return result;
    }
}
