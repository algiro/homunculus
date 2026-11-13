namespace Homunculus.Tools;

/// <summary>The check command a session uses, and the line that tells the user about it.</summary>
public sealed record CheckSelection(string? Command, string? Note)
{
    private static readonly string[] Off = ["off", "none", "false", "no", "0", "disabled"];

    /// <summary>The setting is HOMUNCULUS_CHECK_COMMAND: unset to detect the command, "off" for none, or the command itself.</summary>
    public static CheckSelection Choose(string? setting, string root, CheckDetector detector)
    {
        if (setting is not null)
            return Off.Contains(setting.Trim(), StringComparer.OrdinalIgnoreCase)
                ? new CheckSelection(null, null)
                : new CheckSelection(setting, $"check: {setting}");

        return detector.Detect(root) is { } found
            ? new CheckSelection(found.Command, $"check: {found.Command} (found for {found.Language}; set HOMUNCULUS_CHECK_COMMAND to change it, or to off)")
            : new CheckSelection(null, null);
    }
}
