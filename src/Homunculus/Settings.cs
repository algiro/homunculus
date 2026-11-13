namespace Homunculus;

public sealed record Settings(
    string ApiKey,
    string BaseUrl,
    string Model,
    IterationLimits Iterations,
    IReadOnlyList<string>? EnabledTools = null,
    bool ShowReasoning = true,
    string? LogFile = null,
    string? ReasoningEffort = null)
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    public const string ReasoningEfforts = "none, low, medium or high";

    private static readonly string[] ReasoningEffortValues = ["none", "low", "medium", "high"];

    private static bool IsUnlimited(string text) =>
        text.Equals("unlimited", StringComparison.OrdinalIgnoreCase) || text.Equals("none", StringComparison.OrdinalIgnoreCase);

    public static Settings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static Settings From(Func<string, string?> environment)
    {
        string? Env(string name) => environment(name) is { Length: > 0 } value ? value : null;
        int Number(string name, int fallback) => int.TryParse(Env(name), out var value) ? value : fallback;
        int Limit(string name, int fallback) =>
            Env(name) is { } text && IsUnlimited(text) ? 0 : Number(name, fallback);

        string? Effort(string name) => Env(name) is { } effort
            ? ReasoningEffortValues.FirstOrDefault(value => value.Equals(effort, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{name} must be {ReasoningEfforts}, not '{effort}'.")
            : null;

        var baseUrl = Env("HOMUNCULUS_BASE_URL") ?? DefaultBaseUrl;
        var model = Env("HOMUNCULUS_MODEL") ?? "qwen3:8b";
        var apiKey = Env("OPENAI_API_KEY")
            ?? (baseUrl != DefaultBaseUrl ? "none" : throw new InvalidOperationException("Set OPENAI_API_KEY."));

        return new Settings(
            apiKey,
            baseUrl,
            model,
            new IterationLimits(Limit("HOMUNCULUS_MAX_ITERATIONS", 16)),
            Env("HOMUNCULUS_TOOLS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            !bool.TryParse(Env("HOMUNCULUS_SHOW_REASONING"), out var showReasoning) || showReasoning,
            Env("HOMUNCULUS_LOG_FILE"),
            Effort("HOMUNCULUS_REASONING_EFFORT"));
    }
}
