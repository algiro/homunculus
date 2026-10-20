using Homunculus.Llm;

namespace Homunculus.Tools;

public sealed class ToolRegistry(IEnumerable<ITool> tools)
{
    private readonly Dictionary<string, ITool> _byName = tools.ToDictionary(tool => tool.Name);

    public IReadOnlyCollection<ITool> All => _byName.Values;

    public ITool? Find(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyList<ToolSpec> Specs() =>
        All.Select(tool => new ToolSpec(tool.Name, tool.Description, tool.Parameters)).ToList();
}
