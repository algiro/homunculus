namespace Homunculus.Llm;

/// <summary>
/// What one model call returned: the assistant's message and, when the server reports them, what the call cost
/// and the thinking behind the message. The reasoning is for the user to read; it is never sent back to the model.
/// <paramref name="Elapsed"/> is how long the call took, when something measured it.
/// </summary>
public sealed record Reply(Message Message, TokenUsage? Usage = null, string? Reasoning = null, TimeSpan? Elapsed = null);

/// <summary>Tokens used by one model call. <paramref name="Cached"/> is the part of the input served from the provider's prompt cache.</summary>
public sealed record TokenUsage(int Input, int Output, int Total, int Cached = 0)
{
    public static TokenUsage None { get; } = new(0, 0, 0);

    public TokenUsage Plus(TokenUsage other) =>
        new(Input + other.Input, Output + other.Output, Total + other.Total, Cached + other.Cached);
}
