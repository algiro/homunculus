using System.Text.Json.Nodes;

namespace Homunculus.Tools;

/// <summary>Builds the JSON schema that tells the model which arguments a tool takes.</summary>
public static class Schema
{
    public static JsonObject Object(params Parameter[] parameters) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(parameters.Select(p =>
            KeyValuePair.Create<string, JsonNode?>(p.Name, Property("string", p.Description)))),
        ["required"] = new JsonArray(parameters.Where(p => p.IsRequired).Select(p => (JsonNode)p.Name).ToArray()),
    };

    public static JsonObject ListOfText(string name, string description)
    {
        var list = Property("array", description);
        list["items"] = new JsonObject { ["type"] = "string" };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { [name] = list },
            ["required"] = new JsonArray((JsonNode)name),
        };
    }

    private static JsonObject Property(string type, string description) =>
        new() { ["type"] = type, ["description"] = description };
}
