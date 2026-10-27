namespace Homunculus.Tools;

/// <summary>Finding programs on the PATH.</summary>
public static class Executables
{
    /// <summary>True when a program of that name is on the PATH; on Windows "dotnet" also finds dotnet.exe, "npx" finds npx.cmd and so on.</summary>
    public static bool IsOnPath(string name)
    {
        var extensions = OperatingSystem.IsWindows() && Path.GetExtension(name).Length == 0
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];

        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => extensions.Any(extension => File.Exists(Path.Combine(directory.Trim('"'), name + extension))));
    }
}
