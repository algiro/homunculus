namespace Homunculus.Tools;

/// <summary>The command interpreter that runs the model's command lines: its program, and the arguments that come before the command.</summary>
public sealed record Shell(string Name, string Executable, IReadOnlyList<string> Arguments)
{
    public const string Choices = "pwsh, powershell, cmd or sh";

    private static Shell Cmd { get; } = new("cmd.exe", "cmd.exe", ["/c"]);

    /// <summary>The best shell this machine has: PowerShell (7, else Windows PowerShell) or cmd.exe on Windows, sh elsewhere.</summary>
    public static Shell Detect() => Detect(OperatingSystem.IsWindows(), Executables.IsOnPath);

    public static Shell Detect(bool windows, Func<string, bool> isOnPath)
    {
        if (!windows) return new Shell("sh", "/bin/sh", ["-c"]);
        if (isOnPath("pwsh.exe")) return PowerShell("pwsh.exe");
        if (isOnPath("powershell.exe")) return PowerShell("powershell.exe");
        return Cmd;
    }

    /// <summary>The shell a user asked for by name, with or without ".exe"; null when the name is not one of <see cref="Choices"/>.</summary>
    public static Shell? Named(string name) => WithoutExe(name.Trim().ToLowerInvariant()) switch
    {
        "pwsh" => PowerShell("pwsh.exe"),
        "powershell" => PowerShell("powershell.exe"),
        "cmd" => Cmd,
        "sh" => new Shell("sh", "sh", ["-c"]),
        _ => null,
    };

    private static Shell PowerShell(string executable) =>
        new(executable, executable, ["-NoProfile", "-NonInteractive", "-Command"]);

    private static string WithoutExe(string name) => name.EndsWith(".exe", StringComparison.Ordinal) ? name[..^4] : name;
}
