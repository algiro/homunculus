using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public sealed class ToolException(string message) : Exception(message);

public static class Limits
{
    public const int ListEntries = 200;
    public const int OutputChars = 100_000;
    public const int PlanSteps = 20;
    public const int PlanStepChars = 200;
}

public static class ToolArguments
{
    public static string Text(this JsonElement arguments, string name) =>
        arguments.OptionalText(name) ?? throw new ToolException($"Missing argument '{name}'.");

    public static string? OptionalText(this JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

public static class Schema
{
    /// <summary>An object schema whose properties are all strings; the first <paramref name="required"/> are mandatory.</summary>
    public static JsonObject Strings(int required, params (string Name, string Description)[] properties) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(properties.Select(p => KeyValuePair.Create<string, JsonNode?>(
            p.Name, new JsonObject { ["type"] = "string", ["description"] = p.Description }))),
        ["required"] = new JsonArray(properties.Take(required).Select(p => (JsonNode)p.Name).ToArray()),
    };

    public static JsonObject StringList(string name, string description) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [name] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = description,
                ["items"] = new JsonObject { ["type"] = "string" },
            },
        },
        ["required"] = new JsonArray((JsonNode)name),
    };
}

public static class Text
{
    public static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + $"\n[truncated at {max} characters]";
}
