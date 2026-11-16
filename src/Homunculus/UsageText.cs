using System.Globalization;
using Homunculus.Llm;

namespace Homunculus;

/// <summary>What one turn used: the model, the tokens of all its calls, how many tools ran, and how long it took.</summary>
public sealed record TurnSummary(string? Model, TokenUsage? Usage, int ToolCalls, TimeSpan Elapsed);

/// <summary>How token counts are written for the user.</summary>
public static class UsageText
{
    public static string Describe(TokenUsage usage)
    {
        var cached = usage.Cached > 0 ? $" ({usage.Cached} cached)" : "";
        return $"{usage.Input} in{cached}, {usage.Output} out, {usage.Total} total";
    }

    /// <summary>A count the eye takes in at once: 340, 1.6k, 12k, 1.2M.</summary>
    public static string Compact(int count) => count switch
    {
        < 1_000 => count.ToString(CultureInfo.InvariantCulture),
        < 10_000 => (count / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        < 1_000_000 => (count / 1_000).ToString(CultureInfo.InvariantCulture) + "k",
        _ => (count / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M",
    };

    /// <summary>"qwen3:8b · 1.6k in (813 cached) · 244 out · 2 tools · 45.3s": what one turn used, on one line.</summary>
    public static string Describe(TurnSummary turn)
    {
        var parts = new List<string>();
        if (turn.Model is { Length: > 0 } model) parts.Add(model);
        if (turn.Usage is { } usage)
        {
            parts.Add($"{Compact(usage.Input)} in" + (usage.Cached > 0 ? $" ({Compact(usage.Cached)} cached)" : ""));
            parts.Add($"{Compact(usage.Output)} out");
        }

        if (turn.ToolCalls > 0) parts.Add(turn.ToolCalls == 1 ? "1 tool" : $"{turn.ToolCalls} tools");
        parts.Add(Describe(turn.Elapsed));
        return string.Join(" · ", parts);
    }

    public static string Describe(TimeSpan elapsed) => elapsed.TotalMinutes >= 1
        ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
        : $"{elapsed.TotalSeconds:0.0}s";
}
