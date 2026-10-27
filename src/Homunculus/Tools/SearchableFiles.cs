namespace Homunculus.Tools;

/// <summary>The files a search looks through: everything under a folder except build output, version control and package folders.</summary>
public static class SearchableFiles
{
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", "node_modules",
    };

    /// <summary>The full paths of the files under <paramref name="folder"/>, in a stable order.</summary>
    public static IEnumerable<string> Under(string folder)
    {
        var subfolders = Directory.EnumerateDirectories(folder)
            .Where(subfolder => !SkippedFolders.Contains(Path.GetFileName(subfolder)))
            .Order(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal);

        foreach (var file in files) yield return file;
        foreach (var subfolder in subfolders)
            foreach (var file in Under(subfolder))
                yield return file;
    }
}
