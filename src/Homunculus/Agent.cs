using Homunculus.Llm;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>The whole idea: send the conversation, run any tool the model asks for, repeat until it answers with text.</summary>
public sealed class Agent(
    ILanguageModel model,
    ToolRegistry tools,
    ToolRunner runner,
    IterationLimits limits,
    IUserInterface ui)
{
    private const string SystemPrompt =
        "You are a coding agent working in the user's project directory. Use the tools to inspect and change files " +
        "and to run commands. List a directory before reading files in it, read a file before editing it, " +
        "and keep answers short.";

    private readonly IConversation _conversation = new Conversation(SystemPrompt);

    public void ClearConversation() => _conversation.Clear();

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        _conversation.Add(Message.User(input));
        var specs = tools.Specs();
        var planned = false;

        for (var iteration = 0; iteration < limits.For(planned); iteration++)
        {
            var reply = await model.CompleteAsync(_conversation, specs, cancellation);
            _conversation.Add(reply);

            if (!reply.HasToolCalls)
            {
                ui.Show(reply.Content ?? "");
                return;
            }

            foreach (var call in reply.ToolCalls!)
            {
                planned |= call.Name == PlanTool.ToolName;
                _conversation.Add(Message.ToolResult(call.Id, await runner.RunAsync(call, cancellation)));
            }
        }

        ui.Show("Stopped: iteration limit reached for this message.");
    }
}
