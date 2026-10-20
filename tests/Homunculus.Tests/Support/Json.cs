using System.Text.Json;

namespace Homunculus.Tests.Support;

public static class Json
{
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
