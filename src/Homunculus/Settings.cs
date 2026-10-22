namespace Homunculus;

public sealed record Settings(
    string ApiKey,
    string BaseUrl,
    string Model,
    string JudgeModel,
    TimeSpan CommandTimeout,
    IterationLimits Iterations,
    IReadOnlyList<string>? EnabledTools = null,
    bool ShowReasoning = true)
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    public static Settings FromEnvironment() => From(Environment.GetEnvironmentVariable);

    public static Settings From(Func<string, string?> environment)
    {
        string? Env(string name) => environment(name) is { Length: > 0 } value ? value : null;
        int Number(string name, int fallback) => int.TryParse(Env(name), out var value) ? value : fallback;

        var baseUrl = Env("HOMUNCULUS_BASE_URL") ?? DefaultBaseUrl;
        var model = Env("HOMUNCULUS_MODEL") ?? "gpt-5-mini";
        var apiKey = Env("OPENAI_API_KEY")
            ?? (baseUrl != DefaultBaseUrl ? "none" : throw new InvalidOperationException("Set OPENAI_API_KEY."));

        return new Settings(
            apiKey,
            baseUrl,
            model,
            Env("HOMUNCULUS_JUDGE_MODEL") ?? model,
            TimeSpan.FromSeconds(Number("HOMUNCULUS_COMMAND_TIMEOUT_SECONDS", 600)),
            new IterationLimits(
                Number("HOMUNCULUS_MAX_ITERATIONS", 8),
                Number("HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING", 24)),
            Env("HOMUNCULUS_TOOLS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            !bool.TryParse(Env("HOMUNCULUS_SHOW_REASONING"), out var showReasoning) || showReasoning);
    }
}
