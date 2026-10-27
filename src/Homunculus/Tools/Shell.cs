namespace Homunculus.Tools;

/// <summary>The command interpreter that runs the model's command lines: its program, and the arguments that come before the command.</summary>
public sealed record Shell(string Name, string Executable, IReadOnlyList<string> Arguments)
{
    /// <summary>The best shell this machine has: PowerShell (7, else Windows PowerShell) or cmd.exe on Windows, sh elsewhere.</summary>
    public static Shell Detect() => Detect(OperatingSystem.IsWindows(), IsOnPath);

    public static Shell Detect(bool windows, Func<string, bool> isOnPath)
    {
        if (!windows) return new Shell("sh", "/bin/sh", ["-c"]);
        if (isOnPath("pwsh.exe")) return PowerShell("pwsh.exe");
        if (isOnPath("powershell.exe")) return PowerShell("powershell.exe");
        return new Shell("cmd.exe", "cmd.exe", ["/c"]);
    }

    private static Shell PowerShell(string executable) =>
        new(executable, executable, ["-NoProfile", "-NonInteractive", "-Command"]);

    private static bool IsOnPath(string executable) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Any(directory => File.Exists(Path.Combine(directory.Trim('"'), executable)));
}
