using System.Diagnostics;
using System.Text.Json;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Runs one tool call the model asked for, if approved. Failures the model can act on (a tool's own complaint, a missing file,
/// a locked or forbidden path) become text it can read. Anything else is a bug in a tool, so it is not hidden and propagates.
/// It also notices a model that keeps repeating the same failing call: the second time it is told so, the third time the agent stops.
/// When the user set a check, the guard runs it after every successful change to a file, adds its verdict to the result, and
/// may undo an edit that broke the build; an undone change counts as a failure of the call.
/// </summary>
public sealed class ToolRunner(
    ToolRegistry tools,
    Approvals approvals,
    IUserInterface ui,
    IEventLog? log = null,
    ChangeGuard? guard = null,
    string? root = null)
{
    public const int StopAfterIdenticalFailures = 3;

    private const string RepeatWarning = " You already made this exact call and it failed the same way; repeating it cannot work, so change your approach.";
    private const string RefusalAdvice = " Do not retry it or work around it (for example by changing the code or the policy); tell the user what you need instead.";

    private readonly IEventLog _log = log ?? NullEventLog.Instance;
    private readonly Dictionary<string, int> _failures = [];

    public void ForgetFailures() => _failures.Clear();

    /// <summary>True once the exact same call has failed too many times to be worth another try.</summary>
    public bool IsStuckOn(ToolCall call) => _failures.GetValueOrDefault(KeyOf(call)) >= StopAfterIdenticalFailures;

    /// <summary>Paths are shown relative to this folder, the project's root.</summary>
    private readonly string _projectRoot = root ?? Directory.GetCurrentDirectory();

    public async Task<string> RunAsync(ToolCall call, CancellationToken cancellation)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
        {
            _log.Write("tool_unknown", new { call.Id, call.Name });
            ui.ShowToolCall(call.Name, "");
            var unknown = Failure(call, $"unknown tool '{call.Name}'.");
            ui.ShowToolResult(new ToolOutcome(true, $"unknown tool '{call.Name}'", TimeSpan.Zero));
            return unknown;
        }

        ui.ShowToolCall(call.Name, ToolDisplay.DescribeCall(tool, call.Arguments, _projectRoot));
        _log.Write("tool_call", new { call.Id, call.Name, arguments = call.Arguments });
        string result;
        string shown;
        string? refusal = null;
        ChangeGuard.Verdict? verdict = null;
        var started = Stopwatch.GetTimestamp();
        try
        {
            // The approval may ask the user, so the progress shows only once the tool itself runs.
            refusal = await approvals.CheckAsync(tool, call.Arguments, cancellation);
            if (refusal is null)
            {
                started = Stopwatch.GetTimestamp();
                (result, shown, verdict) = await RunApprovedAsync(tool, call, cancellation);
            }
            else
            {
                // The user sees the refusal as they gave it; the advice added to it is for the model.
                result = Refused(call, refusal);
                shown = refusal;
            }
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            result = Failure(call, e.Message);
            shown = result;
        }

        var outcome = ToolDisplay.DescribeResult(shown, _projectRoot, Stopwatch.GetElapsedTime(started), refused: refusal is not null);
        if (verdict?.Check is { } check) outcome = outcome with { Summary = $"{outcome.Summary} · {check}", Failed = outcome.Failed || verdict.Value.CheckFailed };
        ui.ShowToolResult(outcome);
        if (refusal is null && IsPlan(tool) && result.Length > 0 && !result.StartsWith("Error:", StringComparison.Ordinal))
            ui.Show(result);
        _log.Write("tool_result", new { call.Id, call.Name, result = JsonlEventLog.Truncate(result) });
        return result;
    }

    /// <summary>The result for the model, the tool's own result for the user, and the check's verdict when one ran.</summary>
    private async Task<(string Result, string Shown, ChangeGuard.Verdict? Verdict)> RunApprovedAsync(ITool tool, ToolCall call, CancellationToken cancellation)
    {
        var snapshot = guard?.Before(tool, call.Arguments);
        string result;
        using (ui.ShowProgress("running"))
            result = await tool.RunAsync(call.Arguments, cancellation);

        if (guard is null)
        {
            _failures.Remove(KeyOf(call));
            return (result, result, null);
        }

        ChangeGuard.Verdict verdict;
        using (ui.ShowProgress("checking"))
            verdict = await guard.AfterAsync(snapshot, tool, call.Arguments, result, cancellation);
        if (verdict.Undone) return (Failure(call, verdict.Result), result, verdict);

        _failures.Remove(KeyOf(call));
        return (verdict.Result, result, verdict);
    }

    /// <summary>
    /// The model reads the failure as the tool's result; the user sees its first line under the call, so a later claim of
    /// success can be checked.
    /// </summary>
    private string Failure(ToolCall call, string message) => $"Error: {message}" + RepeatWarningFor(call);

    /// <summary>A recorded plan is shown to the user in full, after the line that says the call went through.</summary>
    private static bool IsPlan(ITool tool) => tool.Name == PlanTool.ToolName;

    /// <summary>The user already knows what they refused, so nothing is shown; the model is told not to argue with it.</summary>
    private string Refused(ToolCall call, string refusal) => refusal + RefusalAdvice + RepeatWarningFor(call);

    private string RepeatWarningFor(ToolCall call)
    {
        var key = KeyOf(call);
        _failures[key] = _failures.GetValueOrDefault(key) + 1;
        return _failures[key] > 1 ? RepeatWarning : "";
    }

    /// <summary>The tool and its arguments, whatever order the model wrote them in.</summary>
    private static string KeyOf(ToolCall call) =>
        call.Arguments.ValueKind == JsonValueKind.Object
            ? call.Name + string.Concat(call.Arguments.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"|{p.Name}={p.Value.GetRawText()}"))
            : call.Name + "|" + call.Arguments.GetRawText();
}
