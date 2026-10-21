using System.Text.Json;
using Homunculus.Llm;
using Homunculus.Tools;

namespace Homunculus.Policy;

/// <summary>A second agent that decides allow, ask or deny, and asks when unsure.</summary>
public sealed class JudgePolicy(ILanguageModel judge) : IPolicy
{
    private const string Instructions =
        "You are a security judge for a coding agent. Given one tool call, answer with exactly one word: " +
        "allow (clearly safe and routine), deny (clearly destructive or malicious), or ask (anything else, or when unsure).";

    public async Task<Verdict> EvaluateAsync(ITool tool, JsonElement arguments, CancellationToken cancellation)
    {
        var conversation = new Conversation(Instructions);
        conversation.Add(Message.User($"Tool: {tool.Name}\nArguments: {arguments.GetRawText()}"));

        var answer = await judge.CompleteAsync(conversation, [], cancellation);
        return Parse(answer.Content);
    }

    private static Verdict Parse(string? answer)
    {
        var word = answer?.Trim().ToLowerInvariant() ?? "";
        if (word.StartsWith("allow", StringComparison.Ordinal)) return Verdict.Allow;
        if (word.StartsWith("deny", StringComparison.Ordinal)) return Verdict.Deny;
        return Verdict.Ask;
    }
}
