using System.Globalization;
using System.Text.Json;

namespace Homunculus.Sec;

/// <summary>
/// What the service needs to run. Read from a JSON file, then overridden by environment variables so that the bot token can stay
/// out of the file (for example in a systemd <c>EnvironmentFile</c> readable only by root).
/// </summary>
public sealed record SecSettings
{
    public string NginxAccessLog { get; init; } = "/var/log/nginx/access.log";
    public string NginxErrorLog { get; init; } = "/var/log/nginx/error.log";
    public string AuthLog { get; init; } = "/var/log/auth.log";

    public string TelegramToken { get; init; } = "";
    public long TelegramChatId { get; init; }

    public string ModelBaseUrl { get; init; } = "http://localhost:11434/v1";
    public string ModelName { get; init; } = "qwen3:8b";
    public string ModelApiKey { get; init; } = "ollama";

    /// <summary>Addresses and CIDR ranges that must never be blocked, on top of loopback and the private ranges.</summary>
    public IReadOnlyList<string> NeverBlock { get; init; } = [];

    public string StatePath { get; init; } = "/var/lib/homunculus-sec/blocks.json";
    public string? EventLogPath { get; init; }
    public int MaxBlockHours { get; init; } = 24 * 7;

    /// <summary>Alert and propose as usual, but never run a firewall command.</summary>
    public bool DryRun { get; init; }

    /// <summary>"iptables" blocks the host and Docker-published ports; "ufw" uses ufw (which must be enabled, and does not reach Docker ports).</summary>
    public string Firewall { get; init; } = "iptables";

    private static readonly JsonSerializerOptions FileOptions = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public const string EnvPrefix = "HOMUNCULUS_SEC_";

    /// <summary>Reads the file (if any) and applies the environment on top.</summary>
    /// <exception cref="FormatException">The file is not valid.</exception>
    public static SecSettings Load(string? path, Func<string, string?> env)
    {
        var settings = new SecSettings();
        if (path is not null)
        {
            try
            {
                settings = JsonSerializer.Deserialize<SecSettings>(File.ReadAllText(path), FileOptions)
                    ?? settings;
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                throw new FormatException($"Cannot read the settings file '{path}': {e.Message}");
            }
        }

        return settings.WithEnvironment(env);
    }

    internal SecSettings WithEnvironment(Func<string, string?> env)
    {
        string? Get(string name) => env(EnvPrefix + name) is { Length: > 0 } value ? value : null;

        var chat = Get("TELEGRAM_CHAT_ID");
        if (chat is not null && !long.TryParse(chat, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new FormatException($"{EnvPrefix}TELEGRAM_CHAT_ID must be a number.");

        var hours = Get("MAX_BLOCK_HOURS");
        if (hours is not null && !int.TryParse(hours, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new FormatException($"{EnvPrefix}MAX_BLOCK_HOURS must be a number.");

        return this with
        {
            TelegramToken = Get("TELEGRAM_TOKEN") ?? TelegramToken,
            TelegramChatId = chat is null ? TelegramChatId : long.Parse(chat, CultureInfo.InvariantCulture),
            ModelBaseUrl = Get("MODEL_BASE_URL") ?? ModelBaseUrl,
            ModelName = Get("MODEL") ?? ModelName,
            ModelApiKey = Get("MODEL_API_KEY") ?? ModelApiKey,
            NeverBlock = Get("NEVER_BLOCK")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? NeverBlock,
            StatePath = Get("STATE_PATH") ?? StatePath,
            EventLogPath = Get("EVENT_LOG") ?? EventLogPath,
            MaxBlockHours = hours is null ? MaxBlockHours : int.Parse(hours, CultureInfo.InvariantCulture),
            DryRun = Get("DRY_RUN") is { } dry ? dry is "1" or "true" or "yes" : DryRun,
            Firewall = Get("FIREWALL")?.ToLowerInvariant() ?? Firewall,
        };
    }

    /// <summary>What is missing or wrong, in words an operator can act on. Empty when the settings are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(TelegramToken)) problems.Add($"The Telegram bot token is missing (set {EnvPrefix}TELEGRAM_TOKEN).");
        if (TelegramChatId == 0) problems.Add($"The Telegram chat id is missing (set {EnvPrefix}TELEGRAM_CHAT_ID).");
        if (Firewall is not ("iptables" or "ufw")) problems.Add("Firewall must be \"iptables\" or \"ufw\".");
        if (MaxBlockHours < 1) problems.Add("MaxBlockHours must be at least 1.");
        if (!Uri.TryCreate(ModelBaseUrl, UriKind.Absolute, out _)) problems.Add("ModelBaseUrl is not a valid URL.");
        return problems;
    }
}
