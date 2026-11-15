namespace Homunculus.Tools;

/// <summary>What counts as a program's name: a bare name such as "dotnet", never a path to a file.</summary>
public static class ProgramName
{
    /// <summary>The only extensions a program is run with on Windows: never .bat or .cmd, which Windows hands to cmd.exe.</summary>
    public static IReadOnlyList<string> WindowsExtensions { get; } = [".exe", ".com"];

    public static bool IsBare(string program) =>
        program.Length > 0
        && program.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, Path.VolumeSeparatorChar]) < 0
        && program is not ("." or "..")
        && !program.Any(char.IsWhiteSpace);

    public static bool HasWindowsExtension(string program) =>
        WindowsExtensions.Contains(Path.GetExtension(program), StringComparer.OrdinalIgnoreCase);
}
