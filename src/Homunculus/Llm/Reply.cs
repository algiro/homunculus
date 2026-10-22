namespace Homunculus.Llm;

/// <summary>What one model call returned: the assistant's message and, when the server reports it, what the call cost.</summary>
public sealed record Reply(Message Message, TokenUsage? Usage = null);

/// <summary>Tokens used by one model call. <paramref name="Cached"/> is the part of the input served from the provider's prompt cache.</summary>
public sealed record TokenUsage(int Input, int Output, int Total, int Cached = 0);
