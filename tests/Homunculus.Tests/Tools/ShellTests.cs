using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ShellTests
{
    private static Shell Detect(bool windows, params string[] installed) => Shell.Detect(windows, installed.Contains);

    [Test]
    public void PrefersPowerShell7OnWindows()
    {
        var shell = Detect(windows: true, "pwsh.exe", "powershell.exe");

        Assert.That(shell.Executable, Is.EqualTo("pwsh.exe"));
        Assert.That(shell.Arguments, Is.EqualTo(new[] { "-NoProfile", "-NonInteractive", "-Command" }));
    }

    [Test]
    public void FallsBackToWindowsPowerShell() =>
        Assert.That(Detect(windows: true, "powershell.exe").Executable, Is.EqualTo("powershell.exe"));

    [Test]
    public void FallsBackToCmdWhenNoPowerShellIsInstalled()
    {
        var shell = Detect(windows: true);

        Assert.That(shell.Name, Is.EqualTo("cmd.exe"));
        Assert.That(shell.Arguments, Is.EqualTo(new[] { "/c" }));
    }

    [Test]
    public void UsesShEverywhereElse()
    {
        var shell = Detect(windows: false, "pwsh.exe");

        Assert.That(shell.Executable, Is.EqualTo("/bin/sh"));
        Assert.That(shell.Arguments, Is.EqualTo(new[] { "-c" }));
    }
}
