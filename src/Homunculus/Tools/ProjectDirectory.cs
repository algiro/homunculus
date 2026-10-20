namespace Homunculus.Tools;

public sealed class ProjectDirectory(string root)
{
    public string Root { get; } = Path.GetFullPath(root);

    public string Resolve(string path) => Path.GetFullPath(path, Root);

    public bool Contains(string fullPath)
    {
        var relative = Path.GetRelativePath(Root, fullPath);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar);
    }
}
