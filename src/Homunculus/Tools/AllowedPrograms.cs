namespace Homunculus.Tools;

/// <summary>
/// The programs run_command may start under the approval mode. Any other program is put to the user every time, in every
/// mode, and can never be allowed for good: it is the line between running the project's own tools and running anything at all
/// (a download, a shell, a script) that text the model read may have asked for.
/// </summary>
public sealed class AllowedPrograms
{
    public static IReadOnlyList<string> Defaults { get; } = ["dotnet", "git"];

    private static readonly StringComparer NameComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly HashSet<string> _names;

    public AllowedPrograms(IEnumerable<string> names)
    {
        Names = names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct(NameComparer).ToList();
        _names = Names.Select(Normalize).ToHashSet(NameComparer);
    }

    public IReadOnlyList<string> Names { get; }

    /// <summary>The names for a message, such as "dotnet, git", or "none".</summary>
    public string Describe() => Names.Count == 0 ? "none" : string.Join(", ", Names);

    /// <summary>True for a bare program name on the list; a path to a program never is, wherever it points.</summary>
    public bool Contains(string program) => ProgramName.IsBare(program) && _names.Contains(Normalize(program));

    /// <summary>On Windows "git" and "git.exe" are the same program.</summary>
    private static string Normalize(string program) =>
        OperatingSystem.IsWindows() && ProgramName.HasWindowsExtension(program) ? Path.GetFileNameWithoutExtension(program) : program;
}
