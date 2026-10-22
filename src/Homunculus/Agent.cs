using Homunculus.Llm;
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
    IUserInterface ui)
{
    private const string Nudge = "You neither called a tool nor gave an answer. Call a tool now, or answer.";

    public void ClearConversation() => conversation.Clear();

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        conversation.Add(Message.User(input));
        var specs = tools.Specs();
        var planned = false;
        var nudged = false;

        for (var iteration = 0; iteration < limits.For(planned); iteration++)
        {
            var reply = await model.CompleteAsync(conversation, specs, cancellation);
            if (showReasoning && reply.Reasoning is { } reasoning) ui.ShowReasoning(reasoning);

            if (IsEmpty(reply.Message))
            {
                // A reasoning model can end its turn after thinking about a tool call it never made.
                // The empty message stays out of the conversation; the model gets one reminder.
                if (nudged)
                {
                    ui.Show("The model returned no answer.");
                    return;
                }

                nudged = true;
                conversation.Add(Message.User(Nudge));
                continue;
            }

            conversation.Add(reply.Message);

            if (!reply.Message.HasToolCalls)
            {
                ui.Show(reply.Message.Content!);
                if (reply.Usage is { } usage) ui.ShowUsage(usage);
                return;
            }

            foreach (var call in reply.Message.ToolCalls!)
            {
                planned |= call.Name == PlanTool.ToolName;
                conversation.Add(Message.ToolResult(call.Id, await runner.RunAsync(call, cancellation)));
            }
        }

        ui.Show("Stopped: iteration limit reached for this message.");
    }

    private static bool IsEmpty(Message message) => !message.HasToolCalls && string.IsNullOrWhiteSpace(message.Content);
}
