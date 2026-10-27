namespace Homunculus.Tools;

public static class StringExtensions
{
    private const double HeadShare = 0.3;

    public static string Truncate(this string text, int max) =>
        text.Length <= max ? text : text[..max] + $"\n[truncated at {max} characters]";

    /// <summary>
    /// Cuts the middle out of a long text and keeps its start and its end: the end of a command's output is where a build
    /// or a test run says what went wrong, and the exit code is the last line.
    /// </summary>
    public static string TruncateKeepingEnd(this string text, int max)
    {
        if (text.Length <= max) return text;

        var head = (int)(max * HeadShare);
        var tail = max - head;
        return text[..head] + $"\n[... {text.Length - max} characters left out ...]\n" + text[^tail..];
    }
}
