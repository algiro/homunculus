using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class PlanTool(Plan plan) : ITool
{
    public const string ToolName = "plan";

    public PlanTool() : this(new Plan())
    {
    }

    public string Name => ToolName;

    public string Description =>
        $"Record the steps of the work ahead (at most {Limits.PlanSteps} steps of {Limits.PlanStepChars} characters). " +
        "Call it again with the full updated list to report progress: start finished steps with [x] and open ones with [ ].";

    public JsonObject Parameters => Schema.ListOfText("steps", "Ordered list of steps.");

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Planning;

    public Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        var steps = arguments.TextList("steps");
        if (steps.Count > Limits.PlanSteps)
            throw new ToolException($"A plan has at most {Limits.PlanSteps} steps.");
        if (steps.Any(step => step.Length > Limits.PlanStepChars))
            throw new ToolException($"A step has at most {Limits.PlanStepChars} characters.");

        plan.Set(steps);
        return Task.FromResult(plan.ToString());
    }
}
