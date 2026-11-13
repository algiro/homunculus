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

    private const string Nudge = "You gave no answer. Answer now.";

    private const string CutOffNudge =
        "Your last reply was cut off: you ran out of room before you finished. Do not think it all through again. " +
        "Answer now in a few sentences.";

    public void ClearConversation() => conversation.Clear();

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        conversation.Add(Message.User(input));
        _log.Write("turn_start", new { input });
        var nudged = false;
        var cutOff = false;
        var calls = 0;

        // Usually one model call. A reply that is empty or cut off earns the model one more try of each kind.
        while (true)
        {
            var reply = await model.CompleteAsync(conversation, cancellation);
            calls++;
            if (showReasoning && reply.Reasoning is { } reasoning) ui.ShowReasoning(reasoning);
            if (reply.Elapsed is { } elapsed) ui.Show($"     model took {UsageText.Describe(elapsed)}");

            _log.Write("reply", new
            {
                iteration = calls - 1,
                reasoning = reply.Reasoning,
                content = reply.Message.Content,
                usage = reply.Usage,
                ms = reply.Elapsed?.TotalMilliseconds,
                cutOff = reply.CutOff,
            });

            if (reply.CutOff)
            {
                // A small model can spend its whole context window thinking. The unfinished reply stays out of the
                // conversation; the model gets one request to answer briefly.
                if (cutOff)
                {
                    _log.Write("turn_end", new { reason = "cut_off_twice" });
                    ui.Show("Stopped: the model's reply was cut off twice because it ran out of room. Try /clear, or ask a narrower question.");
                    return;
                }

                _log.Write("cut_off", new { iteration = calls - 1 });
                cutOff = true;
                conversation.Add(Message.User(CutOffNudge));
                continue;
            }

            if (string.IsNullOrWhiteSpace(reply.Message.Content))
            {
                // The empty message stays out of the conversation; the model gets one reminder.
                if (nudged)
                {
                    _log.Write("turn_end", new { reason = "empty_after_nudge" });
                    ui.Show("The model returned no answer.");
                    return;
                }

                _log.Write("nudge", new { iteration = calls - 1 });
                nudged = true;
                conversation.Add(Message.User(Nudge));
                continue;
            }

            conversation.Add(reply.Message);
            ui.Show(reply.Message.Content);
            if (reply.Usage is { } usage) ui.ShowUsage(usage);
            _log.Write("turn_end", new { reason = "answer", iterations = calls });
            return;
        }
    }
}
