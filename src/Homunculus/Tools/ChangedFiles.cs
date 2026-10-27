namespace Homunculus.Tools;

/// <summary>
/// What the file tools did, for two uses: telling the user at the end of a turn which files changed, and knowing which
/// files the agent created itself (those it may delete without asking).
/// </summary>
public sealed class ChangedFiles(ProjectDirectory project)
{
    private enum Change { Created, Edited, Deleted }

    private readonly HashSet<string> _createdInSession = [];
    private readonly Dictionary<string, Change> _thisTurn = [];

    public void Created(string path)
    {
        _createdInSession.Add(path);
        _thisTurn[path] = _thisTurn.TryGetValue(path, out var earlier) && earlier == Change.Deleted ? Change.Edited : Change.Created;
    }

    public void Edited(string path) => _thisTurn.TryAdd(path, Change.Edited);

    public void Deleted(string path)
    {
        // A file made and removed in the same turn leaves nothing to report.
        if (_thisTurn.TryGetValue(path, out var earlier) && earlier == Change.Created) _thisTurn.Remove(path);
        else _thisTurn[path] = Change.Deleted;
    }

    public bool WasCreatedInSession(string path) => _createdInSession.Contains(path);

    public void StartTurn() => _thisTurn.Clear();

    /// <summary>One line per file changed since <see cref="StartTurn"/>, or null when none was.</summary>
    public string? Summary() => _thisTurn.Count == 0
        ? null
        : "Changed in this turn:\n" + string.Join('\n', _thisTurn
            .OrderBy(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"  {entry.Value.ToString().ToLowerInvariant()} {Path.GetRelativePath(project.Root, entry.Key)}"));
}
