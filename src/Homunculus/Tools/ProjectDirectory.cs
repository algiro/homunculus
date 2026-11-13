namespace Homunculus.Tools;

/// <summary>
/// The folder the agent works in, and the line it may not cross without the user's approval. A path is judged by where it
/// really leads: every symbolic link, junction or mount point on the way is followed first, so a link inside the project that
/// points outside it leads outside, however the path is written.
/// </summary>
public sealed class ProjectDirectory
{
    /// <summary>Links followed for one path before giving up; a cycle of links is judged outside.</summary>
    private const int MaxLinks = 40;

    private readonly string _realRoot;

    public ProjectDirectory(string root)
    {
        Root = Path.GetFullPath(root);
        _realRoot = RealPath(Root) ?? Root;
    }

    public string Root { get; }

    public string Resolve(string path) => Path.GetFullPath(path, Root);

    /// <summary>True when the call reads or writes a file that is not inside the project.</summary>
    public bool IsOutside(ToolEffect effect) =>
        effect is { Path: { } path } && !Contains(path);

    /// <summary>
    /// True when the path, with every link on it followed, is the project or lies inside it. A path that cannot be followed
    /// to the end (a cycle of links, a link that cannot be read) is judged outside, so the user is asked.
    /// </summary>
    public bool Contains(string fullPath) => RealPath(fullPath) is { } real && IsUnder(_realRoot, real);

    private static bool IsUnder(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the path really leads: each part is followed in turn, and a part that is a link is replaced by its target, which is
    /// followed in the same way. Parts that do not exist yet (a file about to be created) are kept as written, since nothing can
    /// redirect them. Returns null when the links cannot be followed to the end.
    /// </summary>
    internal static string? RealPath(string path)
    {
        var links = 0;
        return Follow(Path.GetFullPath(path), ref links);
    }

    private static string? Follow(string fullPath, ref int links)
    {
        var root = Path.GetPathRoot(fullPath)!;
        var parts = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);

            // A link's own entry, not what it points to: this also finds a link whose target is missing, which a write
            // through it would create wherever the link points.
            string? target;
            try
            {
                target = new FileInfo(current).LinkTarget;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (target is null) continue;
            if (++links > MaxLinks) return null;

            // A relative target is relative to the folder holding the link, which is already followed to where it really is.
            var followed = Follow(Path.GetFullPath(target, Path.GetDirectoryName(current)!), ref links);
            if (followed is null) return null;
            current = followed;
        }

        return current;
    }
}
