using Homunculus.Llm;
using Homunculus.Logging;

namespace Homunculus;

/// <summary>The whole idea, at its simplest: add the user's message to the conversation, send it all to the model, show the answer.</summary>
public sealed class Agent(
    ILanguageModel model,
    IConversation conversation,
    bool showReasoning,
    IUserInterface ui,
    IEventLog? log = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;

    public void ClearConversation() => conversation.Clear();

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        conversation.Add(Message.User(input));
        _log.Write("turn_start", new { input });
        var retried = new HashSet<Flaw>();

        // Usually one model call. A flawed reply (cut off, or empty) earns the model one more try of each kind.
        for (var iteration = 0; ; iteration++)
        {
            var reply = await AskModelAsync(iteration, cancellation);

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
            ui.Show(reply.Message.Content!);
            if (reply.Usage is { } usage) ui.ShowUsage(usage);
            _log.Write("turn_end", new { reason = "answer", iterations = iteration + 1 });
            return;
        }
    }

    /// <summary>One round trip: send the whole conversation, show what the model reports about it, log what it said.</summary>
    private async Task<Reply> AskModelAsync(int iteration, CancellationToken cancellation)
    {
        var reply = await model.CompleteAsync(conversation, cancellation);
        if (showReasoning && reply.Reasoning is { } reasoning) ui.ShowReasoning(reasoning);
        if (reply.Elapsed is { } elapsed) ui.Show($"     model took {UsageText.Describe(elapsed)}");

        _log.Write("reply", new
        {
            iteration,
            reasoning = reply.Reasoning,
            content = reply.Message.Content,
            usage = reply.Usage,
            ms = reply.Elapsed?.TotalMilliseconds,
            cutOff = reply.CutOff,
        });
        return reply;
    }

    private static Flaw? FlawOf(Reply reply) =>
        reply.CutOff ? Flaw.CutOff
        : string.IsNullOrWhiteSpace(reply.Message.Content) ? Flaw.Empty
        : null;

    /// <summary>A way a reply can be unusable, and what to say about it: to the model, to the user and to the log.</summary>
    private sealed record Flaw(string LogEvent, string Nudge, string GiveUpReason, string GiveUpMessage)
    {
        // A small model can spend its whole context window thinking and run out of room before it answers.
        public static readonly Flaw CutOff = new(
            "cut_off",
            "Your last reply was cut off: you ran out of room before you finished. Do not think it all through again. " +
            "Answer now in a few sentences.",
            "cut_off_twice",
            "Stopped: the model's reply was cut off twice because it ran out of room. Try /clear, or ask a narrower question.");

        public static readonly Flaw Empty = new(
            "nudge",
            "You gave no answer. Answer now.",
            "empty_after_nudge",
            "The model returned no answer.");
    }
}
