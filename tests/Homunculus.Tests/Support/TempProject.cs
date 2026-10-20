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

    public void Dispose() => System.IO.Directory.Delete(Outside, recursive: true);
}
