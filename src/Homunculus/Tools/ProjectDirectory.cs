namespace Homunculus.Tools;

public sealed class ProjectDirectory(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public string Resolve(string path) => Path.GetFullPath(path, Root);

    /// <summary>True when the call reads, writes or deletes a file that is not inside the project.</summary>
    public bool IsOutside(ToolEffect effect) =>
        effect is { Access: ToolAccess.Read or ToolAccess.Write or ToolAccess.Delete, Path: { } path } && !Contains(path);

    public bool Contains(string fullPath)
    {
        var relative = Path.GetRelativePath(Root, fullPath);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
