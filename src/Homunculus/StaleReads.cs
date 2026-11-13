using Homunculus.Llm;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>
/// Keeps a small context window from filling with files read more than once. Each model call re-sends the whole conversation,
/// so an earlier copy of a file that was read again, or changed since, is replaced by a one-line note. The tool call and its
/// answer stay in place, so the conversation still makes sense.
/// </summary>
public sealed class StaleReads
{
    private readonly Dictionary<string, (string Full, string Shown)> _readsById = [];

    public void Forget() => _readsById.Clear();

    /// <summary>Call after a tool call's answer was added to the conversation.</summary>
    public void AfterCall(IConversation conversation, ToolCall call, ToolEffect? effect, string result)
    {
        if (effect is not { Path: { } path } || result.StartsWith("Error:", StringComparison.Ordinal)) return;

        if (call.Name == ReadFileTool.ToolName)
        {
            Replace(conversation, path, "the file was read again further down");
            _readsById[call.Id] = (path, call.Arguments.OptionalText("path") ?? path);
        }
        else if (effect.Value.Access is ToolAccess.Write)
        {
            Replace(conversation, path, "the file was changed afterwards, so this copy is out of date");
        }
    }

    private void Replace(IConversation conversation, string fullPath, string why)
    {
        foreach (var (id, (_, shown)) in _readsById.Where(entry => entry.Value.Full == fullPath).ToList())
        {
            for (var i = 0; i < conversation.Messages.Count; i++)
            {
                if (conversation.Messages[i] is { Role: Role.Tool } message && message.ToolCallId == id)
                    conversation.Replace(i, Message.ToolResult(id, $"[This earlier read of {shown} was left out: {why}.]"));
            }

            _readsById.Remove(id);
        }
    }
}
