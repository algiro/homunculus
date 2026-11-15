namespace Homunculus.Tools;

/// <summary>
/// Finds the file a program name stands for, on PATH only. Folders inside the project, and relative PATH entries such as ".",
/// are skipped: Windows would otherwise look in the current folder first, so a repository holding its own git.exe would be run
/// in place of the real one.
/// </summary>
public sealed class ProgramLocator(ProjectDirectory project, string? searchPath = null)
{
    /// <summary>The full path of the program, or null when no folder on PATH outside the project has it.</summary>
    public string? Find(string program)
    {
        if (!ProgramName.IsBare(program)) return null;

        var folders = (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => folder.Trim('"'))
            .Where(Path.IsPathFullyQualified)
            .Where(folder => !project.Contains(Path.GetFullPath(folder)));

        foreach (var folder in folders)
            foreach (var candidate in Candidates(program))
            {
                var path = Path.Combine(folder, candidate);
                if (File.Exists(path) && IsExecutable(path)) return path;
            }

        return null;
    }

    private static IEnumerable<string> Candidates(string program) =>
        !OperatingSystem.IsWindows() ? [program]
        : ProgramName.HasWindowsExtension(program) ? [program]
        : ProgramName.WindowsExtensions.Select(extension => program + extension);

    private static bool IsExecutable(string path) =>
        OperatingSystem.IsWindows()
        || (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
}
