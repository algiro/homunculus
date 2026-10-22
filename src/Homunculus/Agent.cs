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
    IEventLog? log = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;

    private const string Nudge = "You neither called a tool nor gave an answer. Call a tool now, or answer.";

    public void ClearConversation() => conversation.Clear();

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        conversation.Add(Message.User(input));
        _log.Write("turn_start", new { input });
        var specs = tools.Specs();
        var planned = false;
        var nudged = false;
        var extended = 0;

        for (var iteration = 0; ; iteration++)
        {
            // At the limit the user decides; each yes grants another full batch (and a plan raises the batch size).
            if (limits.For(planned) is { } batch && iteration >= batch + extended)
            {
                _log.Write("iteration_limit", new { iteration });
                if (ui.Choose($"Reached {iteration} model calls for this message. Continue for {batch} more?", "yn") != 'y')
                {
                    _log.Write("turn_end", new { reason = "iteration_limit" });
                    ui.Show("Stopped: iteration limit reached for this message.");
                    return;
                }

                extended += batch;
            }

            var reply = await model.CompleteAsync(conversation, specs, cancellation);
            if (showReasoning && reply.Reasoning is { } reasoning) ui.ShowReasoning(reasoning);
            if (reply.Elapsed is { } elapsed) ui.Show($"     model took {UsageText.Describe(elapsed)}");

            // What the model said, as parsed, whether or not it is shown: text that comes with tool calls is never displayed.
            _log.Write("reply", new
            {
                iteration,
                reasoning = reply.Reasoning,
                content = reply.Message.Content,
                toolCalls = reply.Message.ToolCalls?.Select(c => c.Name).ToArray(),
                usage = reply.Usage,
                ms = reply.Elapsed?.TotalMilliseconds,
            });

            if (IsEmpty(reply.Message))
            {
                // A reasoning model can end its turn after thinking about a tool call it never made.
                // The empty message stays out of the conversation; the model gets one reminder.
                if (nudged)
                {
                    _log.Write("turn_end", new { reason = "empty_after_nudge" });
                    ui.Show("The model returned no answer.");
                    return;
                }

                _log.Write("nudge", new { iteration });
                nudged = true;
                conversation.Add(Message.User(Nudge));
                continue;
            }

            conversation.Add(reply.Message);

            if (!reply.Message.HasToolCalls)
            {
                ui.Show(reply.Message.Content!);
                if (reply.Usage is { } usage) ui.ShowUsage(usage);
                _log.Write("turn_end", new { reason = "answer", iterations = iteration + 1 });
                return;
            }

            foreach (var call in reply.Message.ToolCalls!)
            {
                planned |= call.Name == PlanTool.ToolName;
                conversation.Add(Message.ToolResult(call.Id, await runner.RunAsync(call, cancellation)));
            }
        }
    }

    private static bool IsEmpty(Message message) => !message.HasToolCalls && string.IsNullOrWhiteSpace(message.Content);
}
