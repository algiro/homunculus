using System.Text.Json;

namespace Homunculus.Tools;

public static class ToolArguments
{
    public static string Text(this JsonElement arguments, string name) =>
        arguments.OptionalText(name) ?? throw new ToolException($"Missing argument '{name}'.");

    public static string? OptionalText(this JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static IReadOnlyList<string> TextList(this JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out var list)
            || list.ValueKind != JsonValueKind.Array)
            throw new ToolException($"Missing argument '{name}'.");

        return list.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new ToolException($"Every item in '{name}' must be text."))
            .ToList();
    }
}
