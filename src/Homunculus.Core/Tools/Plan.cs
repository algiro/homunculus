namespace Homunculus.Tools;

/// <summary>The steps the model last recorded with the plan tool, kept so the user can look at them.</summary>
public sealed class Plan
{
    public IReadOnlyList<string> Steps { get; private set; } = [];

    public void Set(IReadOnlyList<string> steps) => Steps = steps;

    public void Clear() => Steps = [];

    /// <summary>The steps as numbered lines; empty when there are none.</summary>
    public override string ToString() => string.Join('\n', Steps.Select((step, i) => $"{i + 1}. {step}"));
}
