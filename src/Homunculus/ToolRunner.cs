using System.Diagnostics;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Runs one tool call the model asked for, if approved. Failures the model can act on (a tool's own complaint, a missing file,
/// a locked or forbidden path) become text it can read. Anything else is a bug in a tool, so it is not hidden and propagates.
/// It also notices a model that keeps repeating the same failing call: the second time it is told so, the third time the agent stops.
/// </summary>
public sealed class ToolRunner(ToolRegistry tools, Approvals approvals, IUserInterface ui, IEventLog? log = null, string? root = null)
{
    public const int StopAfterIdenticalFailures = 3;

    private readonly IEventLog _log = log ?? NullEventLog.Instance;
    private readonly Dictionary<string, int> _failures = [];

    /// <summary>Paths are shown relative to this folder, the project's root.</summary>
    private readonly string _projectRoot = root ?? Directory.GetCurrentDirectory();

    private readonly Dictionary<string, int> _failures = [];

    public void ForgetFailures() => _failures.Clear();

    /// <summary>True once the exact same call has failed too many times to be worth another try.</summary>
    public bool IsStuckOn(ToolCall call) => _failures.GetValueOrDefault(KeyOf(call)) >= StopAfterIdenticalFailures;
    public async Task<string> RunAsync(ToolCall call, CancellationToken cancellation)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
        {
            _log.Write("tool_unknown", new { call.Id, call.Name });
            return ShowFailure(call, $"unknown tool '{call.Name}'.");
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
                if (tool.EffectOf(call.Arguments).Access == ToolAccess.Plan && result.Length > 0)
                    ui.Show(result);
                _failures.Remove(KeyOf(call));
            }
            else
            {
                result = refusal;
            }
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            result = ShowFailure(call, e.Message);
        }

        ui.ShowToolResult(ToolDisplay.DescribeResult(result, _projectRoot, Stopwatch.GetElapsedTime(started), refused: refusal is not null));
        _log.Write("tool_result", new { call.Id, call.Name, result = JsonlEventLog.Truncate(result) });
        return result;
    }

    /// <summary>The model reads the failure as the tool's result; the user sees it too, so a later claim of success can be checked.</summary>
    private string ShowFailure(ToolCall call, string message)
    {
        var key = KeyOf(call);
        _failures[key] = _failures.GetValueOrDefault(key) + 1;
        var failure = $"Error: {message}";
        if (_failures[key] > 1)
            failure += " You already made this exact call and it failed the same way; repeating it cannot work, so change your approach.";

        ui.Show($"  <- {failure}");
        return failure;
    }

    /// <summary>The tool and its arguments, whatever order the model wrote them in.</summary>
    private static string KeyOf(ToolCall call) =>
        call.Arguments.ValueKind == JsonValueKind.Object
            ? call.Name + string.Concat(call.Arguments.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"|{p.Name}={p.Value.GetRawText()}"))
            : call.Name + "|" + call.Arguments.GetRawText();
}
