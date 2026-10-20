namespace Homunculus;

public sealed record Settings(
    string ApiKey,
    string BaseUrl,
    string Model,
    string JudgeModel,
    TimeSpan CommandTimeout,
    int MaxIterations,
    int MaxIterationsWhenPlanning)
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    public static Settings FromEnvironment()
    {
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
            Number("HOMUNCULUS_MAX_ITERATIONS", 8),
            Number("HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING", 24));
    }

    private static string? Env(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static int Number(string name, int fallback) =>
        int.TryParse(Env(name), out var value) ? value : fallback;
}
