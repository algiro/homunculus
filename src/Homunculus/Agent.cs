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
    ReadFiles? reads = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;
    private readonly StaleReads _stale = new();

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
        var retried = new HashSet<Flaw>();
        var budget = new IterationBudget(limits, ui, _log);
        runner.ForgetFailures();

        while (budget.TryTakeTurn(planned))
        {
            var iteration = budget.Used - 1;
            var reply = await AskModelAsync(specs, iteration, cancellation);

            if (FlawOf(reply) is { } flaw)
            {
                // The flawed reply stays out of the conversation. Add returns false the second time: give up.
                if (!retried.Add(flaw))
                {
                    _log.Write("turn_end", new { reason = flaw.GiveUpReason });
                    ui.Show(flaw.GiveUpMessage);
                    return;
                }

                _log.Write(flaw.LogEvent, new { iteration });
                conversation.Add(Message.User(flaw.Nudge));
                continue;
            }

            conversation.Add(reply.Message);

            if (!reply.Message.HasToolCalls)
            {
                ui.Show(reply.Message.Content!);
                if (reply.Usage is { } usage) ui.ShowUsage(usage);
                _log.Write("turn_end", new { reason = "answer", iterations = budget.Used });
                return;
            }

            foreach (var call in reply.Message.ToolCalls!)
            {
                planned |= call.Name == PlanTool.ToolName;
                var result = await runner.RunAsync(call, cancellation);
                conversation.Add(Message.ToolResult(call.Id, result));
                _stale.AfterCall(conversation, call, EffectOf(call), result);
            }

            if (reply.Message.ToolCalls!.FirstOrDefault(runner.IsStuckOn) is { } stuck)
            {
                _log.Write("turn_end", new { reason = "stuck", tool = stuck.Name });
                ui.Show($"Stopped: the model made the same {stuck.Name} call {ToolRunner.StopAfterIdenticalFailures} times and it failed or was refused each time. Tell it what to do differently.");
                return;
            }
        }

        _log.Write("turn_end", new { reason = "iteration_limit" });
        ui.Show("Stopped: iteration limit reached for this message.");
    }

    /// <summary>One round trip: send the whole conversation, show what the model reports about it, log what it said.</summary>
    private async Task<Reply> AskModelAsync(IReadOnlyList<ToolSpec> specs, int iteration, CancellationToken cancellation)
    {
        var reply = await model.CompleteAsync(conversation, specs, cancellation);
        if (showReasoning && reply.Reasoning is { } reasoning) ui.ShowReasoning(reasoning);
        if (reply.Elapsed is { } elapsed) ui.Show($"     model took {UsageText.Describe(elapsed)}");

        _log.Write("reply", new
        {
            iteration,
            reasoning = reply.Reasoning,
            content = reply.Message.Content,
            toolCalls = reply.Message.ToolCalls?.Select(c => c.Name).ToArray(),
            usage = reply.Usage,
            ms = reply.Elapsed?.TotalMilliseconds,
            cutOff = reply.CutOff,
        });
        return reply;
    }

    private static Flaw? FlawOf(Reply reply) =>
        reply.CutOff ? Flaw.CutOff
        : IsEmpty(reply.Message) ? Flaw.Empty
        : null;

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

    /// <summary>A way a reply can be unusable, and what to say about it: to the model, to the user and to the log.</summary>
    private sealed record Flaw(string LogEvent, string Nudge, string GiveUpReason, string GiveUpMessage)
    {
        // A small model can spend its whole context window thinking and run out of room before it answers.
        public static readonly Flaw CutOff = new(
            "cut_off",
            "Your last reply was cut off: you ran out of room before you finished. Do not think it all through again. " +
            "Answer now in a few sentences from what you already have, or make one tool call.",
            "cut_off_twice",
            "Stopped: the model's reply was cut off twice because it ran out of room. Try /clear, or ask a narrower question.");

        public static readonly Flaw Empty = new(
            "nudge",
            "You neither called a tool nor gave an answer. Call a tool now, or answer.",
            "empty_after_nudge",
            "The model returned no answer.");
    }
}
