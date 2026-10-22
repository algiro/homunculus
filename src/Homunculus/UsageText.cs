using Homunculus.Llm;

namespace Homunculus;

/// <summary>How token counts are written for the user.</summary>
internal static class UsageText
{
    public static string Describe(TokenUsage usage)
    {
        var cached = usage.Cached > 0 ? $" ({usage.Cached} cached)" : "";
        return $"{usage.Input} in{cached}, {usage.Output} out, {usage.Total} total";
    }

    public static string Describe(TimeSpan elapsed) => elapsed.TotalMinutes >= 1
        ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
        : $"{elapsed.TotalSeconds:0.0}s";
}
