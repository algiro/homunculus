using Homunculus.Tools;

namespace Homunculus;

public sealed record Settings(
    string ApiKey,
    string BaseUrl,
    string Model,
    string JudgeModel,
    TimeSpan CommandTimeout,
    IterationLimits Iterations,
    IReadOnlyList<string>? EnabledTools = null,
    bool ShowReasoning = true,
    string? LogFile = null,
    Shell? Shell = null,
    string? ReasoningEffort = null,
    string? JudgeReasoningEffort = null,
    string? CheckCommand = null,
    bool CheckRevert = true,
    string? SearchApiKey = null)
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
            Env("HOMUNCULUS_JUDGE_MODEL") ?? model,
            TimeSpan.FromSeconds(Number("HOMUNCULUS_COMMAND_TIMEOUT_SECONDS", 600)),
            new IterationLimits(
                Limit("HOMUNCULUS_MAX_ITERATIONS", 16),
                Limit("HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING", 32)),
            Env("HOMUNCULUS_TOOLS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            !bool.TryParse(Env("HOMUNCULUS_SHOW_REASONING"), out var showReasoning) || showReasoning,
            Env("HOMUNCULUS_LOG_FILE"),
            Env("HOMUNCULUS_SHELL") is { } shell
                ? Shell.Named(shell) ?? throw new InvalidOperationException($"HOMUNCULUS_SHELL must be {Shell.Choices}, not '{shell}'.")
                : null,
            Effort("HOMUNCULUS_REASONING_EFFORT"),
            Effort("HOMUNCULUS_JUDGE_REASONING_EFFORT"),
            Env("HOMUNCULUS_CHECK_COMMAND"),
            !bool.TryParse(Env("HOMUNCULUS_CHECK_REVERT"), out var checkRevert) || checkRevert,
            Env("HOMUNCULUS_SEARCH_API_KEY"));
    }
}
