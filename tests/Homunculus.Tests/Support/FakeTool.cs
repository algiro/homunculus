using System.Text.Json;
using System.Text.Json.Nodes;
using Homunculus.Tools;

namespace Homunculus.Tests.Support;

/// <summary>A tool that records its calls and answers with a canned result or failure.</summary>
public sealed class FakeTool(string name) : ITool
{
    public string Name => name;
    public string Description => "A fake tool.";
    public JsonObject Parameters => Schema.Of();

    public string Result { get; set; } = "ok";
    public Exception? Failure { get; set; }
    public List<JsonElement> Calls { get; } = [];

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Planning;

    public Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        Calls.Add(arguments);
        return Failure is null ? Task.FromResult(Result) : throw Failure;
    }
}
