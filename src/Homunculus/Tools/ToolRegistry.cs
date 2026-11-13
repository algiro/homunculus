using Homunculus.Llm;

namespace Homunculus.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _byName;

    /// <summary>Holds the available tools, or only those named in <paramref name="enabled"/> (plus the plan tool, which is always on) when given.</summary>
    /// <exception cref="InvalidOperationException">A requested name is not an available tool.</exception>
    public ToolRegistry(IEnumerable<ITool> available, IReadOnlyCollection<string>? enabled = null)
    {
        _byName = available.ToDictionary(tool => tool.Name);
        if (enabled is null) return;

        var unknown = enabled.Where(name => !_byName.ContainsKey(name)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                $"Unknown tool(s): {string.Join(", ", unknown)}. Available: {string.Join(", ", _byName.Keys)}.");

        _byName = _byName
            .Where(pair => pair.Key == PlanTool.ToolName || enabled.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public IReadOnlyCollection<ITool> All => _byName.Values;

    public ITool? Find(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyList<ToolSpec> Specs() =>
        All.Select(tool => new ToolSpec(tool.Name, tool.Description, tool.Parameters)).ToList();
}
