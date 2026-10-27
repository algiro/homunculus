using System.Text.Json;
using Homunculus.Logging;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Runs the user's check after every change to a file and adds its verdict to the tool's result. An edit to a file the agent
/// did not create that turns a passing build into a failing one, or adds errors to a build that was already failing, is
/// undone: a small model that corrupts existing code does not repair it, it digs in. Errors in the agent's own new files
/// are left alone, since it has to iterate on those.
/// </summary>
public sealed class ChangeGuard(
    ProjectCheck check,
    ChangedFiles changes,
    ProjectDirectory project,
    IUserInterface ui,
    IEventLog? log = null,
    bool revertBrokenEdits = true)
{
    private const int MaxErrorsShown = 15;

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

        if (snapshot is not null && !outcome.DidNotFinish && await UndoIfItCausedErrorsAsync(snapshot, outcome, cancellation) is { } undone)
        {
            ui.Show("  change undone");
            _log.Write("change_undone", new { file = Relative(snapshot.Path) });
            return new Verdict("Not applied: " + undone, true);
        }

        return new Verdict(result + "\n\n" + outcome.Report, false);
    }

    /// <summary>
    /// Puts the old content back and checks again. If the build recovers, or the edit added errors the project did not have,
    /// the edit stays undone and the model hears why; otherwise the edit is put back, since it changed nothing for the worse.
    /// </summary>
    private async Task<string?> UndoIfItCausedErrorsAsync(Snapshot snapshot, CheckResult afterEdit, CancellationToken cancellation)
    {
        var edited = File.ReadAllBytes(snapshot.Path);
        File.WriteAllBytes(snapshot.Path, snapshot.Content);
        try
        {
            var before = await check.RunAsync(cancellation);
            var undone = Explain(snapshot, before, afterEdit);
            if (undone is not null)
            {
                if (!snapshot.WasChangedThisTurn) changes.Forget(snapshot.Path);
                return undone;
            }
        }
        catch
        {
            File.WriteAllBytes(snapshot.Path, edited);
            throw;
        }

        File.WriteAllBytes(snapshot.Path, edited);
        return null;
    }

    private string? Explain(Snapshot snapshot, CheckResult before, CheckResult afterEdit)
    {
        var file = Relative(snapshot.Path);
        if (before.Passed)
            return $"this change broke the build, which passed before, so {file} is back as it was.\n{afterEdit.Report}\n" +
                   "Make a change that keeps the build passing, for example create a class before you register it.";

        if (before.DidNotFinish) return null;

        var known = before.Errors.Select(error => error.Key).ToHashSet();
        var added = afterEdit.Errors.Where(error => !known.Contains(error.Key)).Select(error => error.Line).ToList();
        return added.Count == 0
            ? null
            : $"this change added build errors that the project did not have before, so {file} is back as it was. The new errors:\n" +
              $"{string.Join('\n', added.Take(MaxErrorsShown))}\nMake a change that adds no new errors.";
    }

    private string Relative(string path) => Path.GetRelativePath(project.Root, path).Replace('\\', '/');
}
