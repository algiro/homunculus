namespace Homunculus.Tools;

/// <summary>The command interpreter that runs the model's command lines.</summary>
public sealed record Shell(string Name, string Executable, string RunFlag)
{
    public static Shell Detect() =>
        OperatingSystem.IsWindows() ? new Shell("cmd.exe", "cmd.exe", "/c") : new Shell("sh", "/bin/sh", "-c");
}
