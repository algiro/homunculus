namespace Homunculus.Llm;

/// <summary>The tokens one part of the application (such as the agent or the judge) used in the session so far.</summary>
public sealed record SourceUsage(string Source, int Calls, TokenUsage Tokens);

/// <summary>Adds up the tokens every model call reported during the session, per source.</summary>
public sealed class UsageLedger
{
    private readonly SortedDictionary<string, SourceUsage> _bySource = new(StringComparer.Ordinal);

    /// <summary>One entry per source that reported usage, in name order.</summary>
    public IReadOnlyCollection<SourceUsage> Sources => _bySource.Values;

    public void Record(string source, TokenUsage usage)
    {
        var current = _bySource.GetValueOrDefault(source) ?? new SourceUsage(source, 0, TokenUsage.None);
        _bySource[source] = current with { Calls = current.Calls + 1, Tokens = current.Tokens.Plus(usage) };
    }
}
