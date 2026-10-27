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
/// </summary>
public sealed class ToolRunner(ToolRegistry tools, Approvals approvals, IUserInterface ui, IEventLog? log = null)
{
    public const int StopAfterIdenticalFailures = 3;

    private readonly IEventLog _log = log ?? NullEventLog.Instance;
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

        ui.Show($"  -> {call.Name} {call.Arguments.GetRawText()}");
        _log.Write("tool_call", new { call.Id, call.Name, arguments = call.Arguments });
        string result;
        try
        {
            var refusal = await approvals.CheckAsync(tool, call.Arguments, cancellation);
            result = refusal ?? await tool.RunAsync(call.Arguments, cancellation);
            if (refusal is null) _failures.Remove(KeyOf(call));
            if (refusal is null && tool.EffectOf(call.Arguments).Access == ToolAccess.Plan && result.Length > 0)
                ui.Show(result);
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            result = ShowFailure(call, e.Message);
        }

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
