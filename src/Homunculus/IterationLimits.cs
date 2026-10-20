namespace Homunculus;

/// <summary>How many model calls one user message may take, with more room once the agent has made a plan.</summary>
public sealed record IterationLimits(int Normal, int WhenPlanning)
{
    public int For(bool planned) => planned ? WhenPlanning : Normal;
}
