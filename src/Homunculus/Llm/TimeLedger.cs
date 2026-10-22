namespace Homunculus.Llm;

/// <summary>The time one part of the application (such as the agent or the judge) spent waiting for the model in the session so far.</summary>
public sealed record SourceTime(string Source, int Calls, TimeSpan Elapsed);

/// <summary>Adds up how long every model call took during the session, per source.</summary>
public sealed class TimeLedger
{
    private readonly SortedDictionary<string, SourceTime> _bySource = new(StringComparer.Ordinal);

    /// <summary>One entry per source that made a call, in name order.</summary>
    public IReadOnlyCollection<SourceTime> Sources => _bySource.Values;

    public SourceTime? For(string source) => _bySource.GetValueOrDefault(source);

    public TimeSpan Total => _bySource.Values.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.Elapsed);

    public void Record(string source, TimeSpan elapsed)
    {
        var current = _bySource.GetValueOrDefault(source) ?? new SourceTime(source, 0, TimeSpan.Zero);
        _bySource[source] = current with { Calls = current.Calls + 1, Elapsed = current.Elapsed + elapsed };
    }
}
