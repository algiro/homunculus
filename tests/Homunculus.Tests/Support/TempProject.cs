using Homunculus.Tools;

namespace Homunculus.Tests.Support;

/// <summary>A throw-away project folder, deleted on dispose.</summary>
public sealed class TempProject : IDisposable
{
    public TempProject()
    {
        Root = Path.Combine(Path.GetTempPath(), "homunculus-tests-" + Guid.NewGuid().ToString("N"), "project");
        System.IO.Directory.CreateDirectory(Root);
        Location = new ProjectDirectory(Root);
    }

    public string Root { get; }
    public ProjectDirectory Location { get; }

    private readonly List<string> _links = [];

    /// <summary>A folder next to the project, so it is outside it.</summary>
    public string Outside => System.IO.Directory.GetParent(Root)!.FullName;

    public string PathOf(string relative) => Path.Combine(Root, relative);

    public string Write(string relative, string content)
    {
        var path = PathOf(relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Read(string relative) => File.ReadAllText(PathOf(relative));

    public bool Exists(string relative) => File.Exists(PathOf(relative)) || System.IO.Directory.Exists(PathOf(relative));

    /// <summary>
    /// Makes <paramref name="relative"/> a link to the folder <paramref name="target"/>: a symbolic link where the system allows
    /// one, otherwise (Windows without the privilege) a junction, which any user can create. Skips the test when neither works.
    /// </summary>
    public string LinkDirectory(string relative, string target)
    {
        var link = Path.GetFullPath(PathOf(relative));
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        _links.Add(link);
        try
        {
            System.IO.Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && OperatingSystem.IsWindows())
        {
            CreateJunction(link, target);
        }

        return link;
    }

    /// <summary>Makes <paramref name="relative"/> a symbolic link to the file <paramref name="target"/>, or skips the test when the system refuses.</summary>
    public string LinkFile(string relative, string target)
    {
        var link = Path.GetFullPath(PathOf(relative));
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        _links.Add(link);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"This system does not let the tests create a file symbolic link: {e.Message}");
        }

        return link;
    }

    private static void CreateJunction(string link, string target)
    {
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        mklink.WaitForExit();
        if (mklink.ExitCode != 0) Assert.Ignore($"Could not create a junction: {mklink.StandardError.ReadToEnd()}");
    }

    /// <summary>Links go first and on their own, so deleting the folder never walks through one to wherever it points.</summary>
    public void Dispose()
    {
        foreach (var link in _links.Select(path => new FileInfo(path)).Where(link => link.LinkTarget is not null))
        {
            // The attributes are the link's own, so a link to a folder says Directory even when its target is gone.
            if (link.Attributes.HasFlag(FileAttributes.Directory)) System.IO.Directory.Delete(link.FullName);
            else link.Delete();
        }

        System.IO.Directory.Delete(Outside, recursive: true);
    }
}
