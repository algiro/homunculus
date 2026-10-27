using System.Text.Json;
using Homunculus.Logging;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Runs the user's check after every change to a file and adds its verdict to the tool's result. An edit to a file the agent
/// did not create that turns a passing build into a failing one is undone: a small model that corrupts existing code does
/// not repair it, it digs in. Errors in the agent's own new files are left alone, since it has to iterate on those.
/// </summary>
public sealed class ChangeGuard(
    ProjectCheck check,
    ChangedFiles changes,
    ProjectDirectory project,
    IUserInterface ui,
    IEventLog? log = null,
    bool revertBrokenEdits = true)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;

    /// <summary>What a file held before an edit, so the edit can be taken back.</summary>
    public sealed record Snapshot(string Path, byte[] Content, bool WasChangedThisTurn);

    /// <summary>The result to give the model, and whether the change was undone (which makes it a failure).</summary>
    public readonly record struct Verdict(string Result, bool Undone);

    /// <summary>Keeps the file's content when the call may edit an existing file of the user's; null otherwise.</summary>
    public Snapshot? Before(ITool tool, JsonElement arguments) =>
        revertBrokenEdits
        && tool.EffectOf(arguments) is { Access: ToolAccess.Write, Path: { } path }
        && File.Exists(path)
        && !changes.WasCreatedInSession(path)
            ? new Snapshot(path, File.ReadAllBytes(path), changes.IsChangedThisTurn(path))
            : null;

    public async Task<Verdict> AfterAsync(Snapshot? snapshot, ITool tool, JsonElement arguments, string result, CancellationToken cancellation)
    {
        if (tool.EffectOf(arguments).Access is not (ToolAccess.Write or ToolAccess.Delete)) return new Verdict(result, false);

        var outcome = await check.RunAsync(cancellation);
        ui.Show(outcome.Passed ? "  check: passed" : "  check: failed");
        _log.Write("check", new { outcome.Passed });
        if (outcome.Passed) return new Verdict(result + "\n\n" + outcome.Report, false);

        if (snapshot is not null && await BrokeAPassingBuildAsync(snapshot, cancellation))
        {
            ui.Show("  change undone");
            _log.Write("change_undone", new { file = Relative(snapshot.Path) });
            return new Verdict(
                $"Not applied: this change broke the build, which passed before, so {Relative(snapshot.Path)} is back as it was.\n" +
                $"{outcome.Report}\nMake a change that keeps the build passing, for example create a class before you register it.",
                true);
        }

        return new Verdict(result + "\n\n" + outcome.Report, false);
    }

    /// <summary>Puts the old content back and checks again: if the build recovers the edit was the cause and stays undone.</summary>
    private async Task<bool> BrokeAPassingBuildAsync(Snapshot snapshot, CancellationToken cancellation)
    {
        var edited = File.ReadAllBytes(snapshot.Path);
        File.WriteAllBytes(snapshot.Path, snapshot.Content);
        try
        {
            if ((await check.RunAsync(cancellation)).Passed)
            {
                if (!snapshot.WasChangedThisTurn) changes.Forget(snapshot.Path);
                return true;
            }
        }
        catch
        {
            File.WriteAllBytes(snapshot.Path, edited);
            throw;
        }

        File.WriteAllBytes(snapshot.Path, edited);
        return false;
    }

    private string Relative(string path) => Path.GetRelativePath(project.Root, path).Replace('\\', '/');
}
