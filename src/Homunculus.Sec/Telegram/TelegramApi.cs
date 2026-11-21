using System.Text;
using System.Text.Json.Nodes;

namespace Homunculus.Sec.Telegram;

/// <summary>
/// The few Bot API methods the service needs, over long polling (no public endpoint is required). The bot token is part of every
/// URL, so it is never put in an error message or a log.
/// </summary>
public sealed class TelegramApi(HttpClient http, string token) : ITelegramApi
{
    /// <summary>How long an ordinary call may take. A long poll holds the connection for its 30 s, so it gets more.</summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Connects IPv4 first with a short per-address deadline (see <see cref="TelegramConnect"/>); each call has its own deadline.</summary>
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { ConnectCallback = TelegramConnect.ConnectAsync, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        BaseAddress = new Uri("https://api.telegram.org/"),
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<IReadOnlyList<Update>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellation)
    {
        var result = await CallAsync("getUpdates", new JsonObject
        {
            ["offset"] = offset,
            ["timeout"] = timeoutSeconds,
            ["allowed_updates"] = new JsonArray("message", "callback_query"),
        }, cancellation);
        return ParseUpdates(result);
    }

    public async Task<long> SendMessageAsync(long chatId, string html, IReadOnlyList<Button>? buttons, CancellationToken cancellation)
    {
        var body = new JsonObject
        {
            ["chat_id"] = chatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = true,
        };
        if (buttons is { Count: > 0 })
            body["reply_markup"] = new JsonObject { ["inline_keyboard"] = new JsonArray(new JsonArray(buttons.Select(ButtonJson).ToArray())) };

        var result = await CallAsync("sendMessage", body, cancellation);
        return result?["message_id"]?.GetValue<long>() ?? throw new TelegramException("Telegram did not return a message id.");
    }

    public Task AnswerPressAsync(string pressId, string? text, CancellationToken cancellation) =>
        CallAsync("answerCallbackQuery", new JsonObject { ["callback_query_id"] = pressId, ["text"] = text }, cancellation);

    public Task ClearButtonsAsync(long chatId, long messageId, CancellationToken cancellation) =>
        CallAsync("editMessageReplyMarkup", new JsonObject
        {
            ["chat_id"] = chatId,
            ["message_id"] = messageId,
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = new JsonArray() },
        }, cancellation);

    /// <summary>Reads the <c>result</c> array of a getUpdates response. Updates of other kinds are skipped.</summary>
    public static IReadOnlyList<Update> ParseUpdates(JsonNode? result)
    {
        var updates = new List<Update>();
        if (result is not JsonArray array) return updates;

        foreach (var item in array)
        {
            if (item?["update_id"]?.GetValue<long>() is not { } id) continue;

            if (item["message"] is JsonNode message && message["chat"]?["id"]?.GetValue<long>() is { } chatId)
            {
                updates.Add(new Update(id, new IncomingMessage(
                    chatId,
                    message["from"]?["id"]?.GetValue<long>() ?? 0,
                    message["message_id"]?.GetValue<long>() ?? 0,
                    message["text"]?.GetValue<string>(),
                    message["reply_to_message"]?["message_id"]?.GetValue<long>()), null));
            }
            else if (item["callback_query"] is JsonNode press)
            {
                updates.Add(new Update(id, null, new ButtonPress(
                    press["id"]?.GetValue<string>() ?? "",
                    press["from"]?["id"]?.GetValue<long>() ?? 0,
                    press["message"]?["chat"]?["id"]?.GetValue<long>() ?? 0,
                    press["message"]?["message_id"]?.GetValue<long>() ?? 0,
                    press["data"]?.GetValue<string>())));
            }
            else
            {
                updates.Add(new Update(id, null, null));
            }
        }

        return updates;
    }

    private static JsonNode ButtonJson(Button button) => new JsonObject { ["text"] = button.Text, ["callback_data"] = button.Data };

    private async Task<JsonNode?> CallAsync(string method, JsonObject body, CancellationToken cancellation)
    {
        try
        {
            // The leading slash matters: without it "bot123456:AAH.../method" reads as an absolute URI whose scheme is "bot123456".
            // A string body has a known length, so it is sent with Content-Length. PostAsJsonAsync streams it chunked, and the
            // Bot API then never answers a request with a valid token.
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(method == "getUpdates" ? PollTimeout : CallTimeout);
            using var response = await http.PostAsync($"/bot{token}/{method}", content, deadline.Token);
            var text = await response.Content.ReadAsStringAsync(deadline.Token);
            var json = JsonNode.Parse(text);
            if (json?["ok"]?.GetValue<bool>() != true)
                throw new TelegramException($"Telegram {method} failed ({(int)response.StatusCode}): {json?["description"]?.GetValue<string>()}");
            return json["result"];
        }
        catch (TelegramException)
        {
            throw;
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new TelegramException($"Telegram {method} timed out.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Never e.Message: it can carry the request URL, and the URL carries the token.
            throw new TelegramException($"Telegram {method} failed: {e.GetType().Name}.");
        }
    }
}
