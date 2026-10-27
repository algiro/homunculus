namespace Homunculus.Tools;

/// <summary>The files the model has read since the conversation began, so it can be reminded to look at a neighbour before it writes a new file.</summary>
public sealed class ReadFiles
{
    private readonly HashSet<string> _read = [];

    public void Read(string path) => _read.Add(path);

    public bool HasReadAnyIn(string directory) => _read.Any(path => Path.GetDirectoryName(path) == directory);

    public void Forget() => _read.Clear();
}
