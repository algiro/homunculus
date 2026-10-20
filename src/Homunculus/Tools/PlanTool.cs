using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class PlanTool : ITool
{
    public const string ToolName = "plan";

    public string Name => ToolName;
    public string Description => $"Record the steps of the work ahead (at most {Limits.PlanSteps} steps of {Limits.PlanStepChars} characters).";
    public JsonObject Parameters => Schema.ListOfText("steps", "Ordered list of steps.");

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Planning;

    public Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("steps", out var list)
            || list.ValueKind != JsonValueKind.Array)
            throw new ToolException("Missing argument 'steps'.");

        var steps = list.EnumerateArray()
            .Select(step => step.ValueKind == JsonValueKind.String ? step.GetString()! : throw new ToolException("Every step must be text."))
            .ToList();
        if (steps.Count > Limits.PlanSteps)
            throw new ToolException($"A plan has at most {Limits.PlanSteps} steps.");
        if (steps.Any(step => step.Length > Limits.PlanStepChars))
            throw new ToolException($"A step has at most {Limits.PlanStepChars} characters.");

        return Task.FromResult(string.Join('\n', steps.Select((step, i) => $"{i + 1}. {step}")));
    }
}
