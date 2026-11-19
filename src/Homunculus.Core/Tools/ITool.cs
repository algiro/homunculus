using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonObject Parameters { get; }

    ToolEffect EffectOf(JsonElement arguments);

    Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation);
}
