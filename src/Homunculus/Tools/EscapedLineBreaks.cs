namespace Homunculus.Tools;

/// <summary>A model that escapes its own escapes sends the two characters backslash and n where it means a line break.</summary>
public static class EscapedLineBreaks
{
    /// <summary>True for text with no real line break that contains the characters backslash and n at least twice.</summary>
    public static bool Look(string text) => !text.Contains('\n') && text.Split("\\n").Length > 2;

    public static string Complaint(string argument) =>
        $"{argument} has no line breaks but contains the two characters backslash and n. Write real line breaks, not the characters \\n.";
}
