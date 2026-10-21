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
    IUserInterface ui)
{
    public void ClearConversation() => conversation.Clear();

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        conversation.Add(Message.User(input));
        var specs = tools.Specs();
        var planned = false;

        for (var iteration = 0; iteration < limits.For(planned); iteration++)
        {
            var reply = await model.CompleteAsync(conversation, specs, cancellation);
            conversation.Add(reply);

            if (!reply.HasToolCalls)
            {
                ui.Show(reply.Content ?? "");
                return;
            }

            foreach (var call in reply.ToolCalls!)
            {
                planned |= call.Name == PlanTool.ToolName;
                conversation.Add(Message.ToolResult(call.Id, await runner.RunAsync(call, cancellation)));
            }
        }

        ui.Show("Stopped: iteration limit reached for this message.");
    }
}
