using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>The whole idea: send the conversation, run any tool the model asks for, repeat until it answers with text.</summary>
public sealed class Agent(
    ILanguageModel model,
    ToolRegistry tools,
    Approvals approvals,
    IUserInterface ui,
    Settings settings)
{
    private const string SystemPrompt =
        "You are a coding agent working in the user's project directory. Use the tools to inspect and change files " +
        "and to run commands. List a directory before reading files in it, read a file before editing it, " +
        "and keep answers short.";

    private readonly List<Message> _conversation = [Message.System(SystemPrompt)];

    public void Clear()
    {
        _conversation.Clear();
        _conversation.Add(Message.System(SystemPrompt));
    }

    public async Task RespondAsync(string input, CancellationToken cancellation)
    {
        _conversation.Add(Message.User(input));
        var specs = tools.Specs();
        var planned = false;

        for (var iteration = 0; iteration < Ceiling(planned); iteration++)
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
                _conversation.Add(Message.ToolResult(call.Id, await RunToolAsync(call, cancellation)));
            }
        }

        ui.Show("Stopped: iteration limit reached for this message.");
    }

    private int Ceiling(bool planned) => planned ? settings.MaxIterationsWhenPlanning : settings.MaxIterations;

    private async Task<string> RunToolAsync(ToolCall call, CancellationToken cancellation)
    {
        var tool = tools.Find(call.Name);
        if (tool is null)
            return $"Error: unknown tool '{call.Name}'.";

        ui.Show($"  -> {call.Name} {call.Arguments.GetRawText()}");
        try
        {
            var refusal = await approvals.CheckAsync(tool, call.Arguments, cancellation);
            return refusal ?? await tool.RunAsync(call.Arguments, cancellation);
        }
        catch (Exception e) when (e is ToolException or IOException or UnauthorizedAccessException)
        {
            return $"Error: {e.Message}";
        }
    }
}
