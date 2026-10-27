namespace Homunculus.Tools;

/// <summary>Creates a file that did not exist, and says which files the new one builds on that the model has not read.</summary>
public sealed class NewFileWriter(ProjectDirectory project, ChangedFiles? changes = null, ReadFiles? reads = null)
{
    private const int MaxNamedFiles = 5;

    /// <summary>Creates the file; <paramref name="ifExists"/> is what the model is told when the file is already there.</summary>
    public string Create(string path, string content, string ifExists)
    {
        if (File.Exists(path))
            throw new ToolException($"File already exists: {path}. {ifExists}");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        changes?.Created(path);
        return $"Created {path}{UnreadFilesNote(path, content)}";
    }

    /// <summary>
    /// A new file is better written after looking at what it builds on. First choice: files of the same kind (same extension)
    /// named after a name the new code uses (ICommand for ": ICommand"), found in its folder or above it, that the model has
    /// not read. Failing that, any neighbour at all.
    /// </summary>
    private string UnreadFilesNote(string path, string content)
    {
        if (reads is null) return "";

        var folder = Path.GetDirectoryName(path)!;
        var used = Identifiers.In(content);
        var extension = Path.GetExtension(path);
        var unread = FoldersFrom(folder)
            .SelectMany(Directory.EnumerateFiles)
            .Where(other => other != path && Path.GetExtension(other) == extension
                            && used.Contains(Path.GetFileNameWithoutExtension(other)) && !reads.HasRead(other))
            .Distinct().Order(StringComparer.Ordinal).Take(MaxNamedFiles).ToArray();
        if (unread.Length > 0)
        {
            var names = string.Join(", ", unread.Select(Path.GetFileNameWithoutExtension).Distinct());
            var files = string.Join(", ", unread.Select(Relative));
            return $". It uses {names}, defined in {files}, which you have not read. Read them and check that this file matches.";
        }

        var hasNeighbours = Directory.EnumerateFiles(folder).Any(other => other != path);
        return !hasNeighbours || reads.HasReadAnyIn(folder)
            ? ""
            : $". You have not read any other file in {Relative(folder)} yet; if this file should follow the ones there, read one and compare.";
    }

    /// <summary>The folder and each folder above it, up to the project root.</summary>
    private IEnumerable<string> FoldersFrom(string folder)
    {
        for (var current = folder; current is not null && project.Contains(current); current = Path.GetDirectoryName(current))
            yield return current;
    }

    private string Relative(string fullPath) => Path.GetRelativePath(project.Root, fullPath).Replace('\\', '/');
}
