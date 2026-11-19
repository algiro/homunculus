using System.Diagnostics;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>The whole idea: send the conversation, run any tool the model asks for, repeat until it answers with text.</summary>
public sealed class Agent(
    ILanguageModel model,
    IConversation conversation,
    ToolRegistry tools,
    ToolRunner runner,
    IterationLimits limits,
    bool showReasoning,
    IUserInterface ui,
    IEventLog? log = null,
    ChangedFiles? changes = null,
    ReadFiles? reads = null,
    string? modelName = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;
    private readonly StaleReads _stale = new();

    private const string Nudge = "You neither called a tool nor gave an answer. Call a tool now, or answer.";

    private const string CutOffNudge =
        "Your last reply was cut off: you ran out of room before you finished. Do not think it all through again. " +
        "Answer now in a few sentences from what you already have, or make one tool call.";

    public void ClearConversation()
    {
        conversation.Clear();
        reads?.Forget();
        _stale.Forget();
    }

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        changes?.StartTurn();
        try
        {
            await RunTurnAsync(input, cancellation);
        }
        finally
        {
            // However the turn ended, the user sees which files it touched.
            if (changes?.Summary() is { } summary) ui.Show(summary);
        }
    }

    private async Task RunTurnAsync(string input, CancellationToken cancellation)
    {
        conversation.Add(Message.User(input));
        _log.Write("turn_start", new { input });
        var specs = tools.Specs();
        var planned = false;
        var nudged = false;
        var cutOff = false;
        var budget = new IterationBudget(limits, ui, _log);
        var started = Stopwatch.GetTimestamp();
        TokenUsage? used = null;
        var toolCalls = 0;
        void Summarise() => ui.ShowTurnSummary(new TurnSummary(modelName, used, toolCalls, Stopwatch.GetElapsedTime(started)));
        runner.ForgetFailures();

        while (budget.TryTakeTurn(planned))
        {
            Reply reply;
            using (ui.ShowProgress("thinking"))
                reply = await model.CompleteAsync(conversation, specs, cancellation);
            if (reply.Usage is { } callUsage) used = used is null ? callUsage : used.Plus(callUsage);
            if (showReasoning && reply.Reasoning is { } reasoning) ui.ShowReasoning(reasoning);
            if (reply.Elapsed is { } elapsed) ui.Show($"     model took {UsageText.Describe(elapsed)}");

            // What the model said, as parsed, whether or not it is shown: text that comes with tool calls is never displayed.
            _log.Write("reply", new
            {
                iteration = budget.Used - 1,
                reasoning = reply.Reasoning,
                content = reply.Message.Content,
                toolCalls = reply.Message.ToolCalls?.Select(c => c.Name).ToArray(),
                usage = reply.Usage,
                ms = reply.Elapsed?.TotalMilliseconds,
                cutOff = reply.CutOff,
            });

            if (reply.CutOff)
            {
                // A small model can spend its whole context window thinking. The unfinished reply stays out of the
                // conversation; the model gets one request to answer briefly from what it has.
                if (cutOff)
                {
                    _log.Write("turn_end", new { reason = "cut_off_twice" });
                    ui.Show("Stopped: the model's reply was cut off twice because it ran out of room. Try /clear, or ask a narrower question.");
                    Summarise();
                    return;
                }

                _log.Write("cut_off", new { iteration = budget.Used - 1 });
                cutOff = true;
                conversation.Add(Message.User(CutOffNudge));
                continue;
            }

            if (IsEmpty(reply.Message))
            {
                // A reasoning model can end its turn after thinking about a tool call it never made.
                // The empty message stays out of the conversation; the model gets one reminder.
                if (nudged)
                {
                    _log.Write("turn_end", new { reason = "empty_after_nudge" });
                    ui.Show("The model returned no answer.");
                    Summarise();
                    return;
                }

                _log.Write("nudge", new { iteration = budget.Used - 1 });
                nudged = true;
                conversation.Add(Message.User(Nudge));
                continue;
            }

            conversation.Add(reply.Message);

            if (!reply.Message.HasToolCalls)
            {
                ui.ShowAnswer(reply.Message.Content!);
                Summarise();
                _log.Write("turn_end", new { reason = "answer", iterations = budget.Used });
                return;
            }

            foreach (var call in reply.Message.ToolCalls!)
            {
                planned |= call.Name == PlanTool.ToolName;
                toolCalls++;
                var result = await runner.RunAsync(call, cancellation);
                conversation.Add(Message.ToolResult(call.Id, result));
                _stale.AfterCall(conversation, call, EffectOf(call), result);
            }

            if (reply.Message.ToolCalls!.FirstOrDefault(runner.IsStuckOn) is { } stuck)
            {
                _log.Write("turn_end", new { reason = "stuck", tool = stuck.Name });
                ui.Show($"Stopped: the model made the same {stuck.Name} call {ToolRunner.StopAfterIdenticalFailures} times and it failed or was refused each time. Tell it what to do differently.");
                Summarise();
                return;
            }
        }

        _log.Write("turn_end", new { reason = "iteration_limit" });
        ui.Show("Stopped: iteration limit reached for this message.");
        Summarise();
    }

    /// <summary>What the call does to a file, or null when the call is not one the tools can describe.</summary>
    private ToolEffect? EffectOf(ToolCall call)
    {
        try
        {
            return tools.Find(call.Name)?.EffectOf(call.Arguments);
        }
        catch (ToolException)
        {
            return null;
        }
    }

    private static bool IsEmpty(Message message) => !message.HasToolCalls && string.IsNullOrWhiteSpace(message.Content);
}
