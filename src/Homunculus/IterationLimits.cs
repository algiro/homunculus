namespace Homunculus;

/// <summary>How many model calls one user message may take before the user is asked whether to go on. A value of 0 (or less) means no limit.</summary>
public sealed record IterationLimits(int Normal)
{
    public static IterationLimits Unlimited { get; } = new(0);

    /// <summary>The calls allowed before asking, or null when there is no limit.</summary>
    public int? Limit => Normal > 0 ? Normal : null;
}
