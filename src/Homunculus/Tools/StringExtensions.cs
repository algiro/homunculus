namespace Homunculus.Tools;

public static class StringExtensions
{
    public static string Truncate(this string text, int max) =>
        text.Length <= max ? text : text[..max] + $"\n[truncated at {max} characters]";
}
