namespace Homunculus.Tools;

public static class Limits
{
    public const int ListEntries = 200;
    public const int SearchResults = 200;
    public const int OutputChars = 100_000;

    /// <summary>A command's output, kept short enough for a small context window; a build or test run ends with its summary.</summary>
    public const int CommandOutputChars = 20_000;
    public const int PlanSteps = 20;
    public const int PlanStepChars = 200;
}
