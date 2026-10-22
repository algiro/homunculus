namespace Homunculus;

/// <summary>
/// How many model calls one user message may take before the user is asked whether to go on, with more room once the agent
/// has made a plan. A value of 0 (or less) means no limit.
/// </summary>
public sealed record IterationLimits(int Normal, int WhenPlanning)
{
    public static IterationLimits Unlimited { get; } = new(0, 0);

    /// <summary>The calls allowed before asking, or null when there is no limit.</summary>
    public int? For(bool planned)
    {
        var limit = planned ? WhenPlanning : Normal;
        return limit > 0 ? limit : null;
    }
}
