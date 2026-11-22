using System.Net;
using Homunculus.Logging;
using Homunculus.Sec.Analysis;
using Homunculus.Sec.Firewall;
using Homunculus.Sec.Safety;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Service;

/// <summary>
/// Handles what the operator sends: button presses, yes/no replies, a few commands, and free-text questions. Only the configured
/// chat is obeyed; anything from anyone else is dropped without an answer. Nothing but an explicit yes can cause a block.
/// </summary>
public sealed class ChatHandler(
    ITelegramApi telegram,
    long chatId,
    IncidentBook book,
    BlockManager blocks,
    Analyst analyst,
    Allowlist allowlist,
    IEventLog? log = null)
{
    private static readonly string[] Yes = ["yes", "y", "ok", "approve", "block", "si", "sí", "sim", "confirm"];
    private static readonly string[] No = ["no", "n", "ignore", "skip", "cancel", "dismiss"];

    private const string Help =
        "/pending – alerts waiting for an answer\n/blocks – addresses currently blocked\n/unblock &lt;ip&gt; – remove a block\n" +
        "Or reply yes / no to an alert, or ask a question about it.";

    public async Task HandleAsync(Update update, CancellationToken cancellation)
    {
        if (update.Press is { } press)
        {
            if (!Authorised(press.FromId, press.ChatId)) return;
            await HandlePressAsync(press, cancellation);
        }
        else if (update.Message is { Text: { } } message)
        {
            if (!Authorised(message.FromId, message.ChatId)) return;
            await HandleTextAsync(message, cancellation);
        }
    }

    private bool Authorised(long fromId, long fromChat)
    {
        if (fromId == chatId && fromChat == chatId) return true;
        log?.Write("telegram_unauthorised", new { fromId, fromChat });
        return false;
    }

    private async Task HandlePressAsync(ButtonPress press, CancellationToken cancellation)
    {
        var parts = (press.Data ?? "").Split(':', 2);
        var incident = parts.Length == 2 ? book.Find(parts[1]) : null;
        if (incident is null)
        {
            // Almost always a button on an alert from before the service restarted: alerts waiting for an answer are kept in memory.
            log?.Write("telegram_press_unknown", new { press.Data });
            await telegram.AnswerPressAsync(press.Id, "This alert is from before the service restarted and is no longer active. A new alert will come if it happens again.", cancellation);
            await ClearButtonsQuietlyAsync(press.ChatId, press.MessageId, cancellation);
            return;
        }

        string answer = parts[0] switch
        {
            "a" => await ApproveAsync(incident, cancellation),
            "i" => await DismissAsync(incident, cancellation),
            _ => "Unknown action.",
        };
        await telegram.AnswerPressAsync(press.Id, answer, cancellation);
    }

    private async Task HandleTextAsync(IncomingMessage message, CancellationToken cancellation)
    {
        var text = message.Text!.Trim();
        var word = text.TrimEnd('.', '!').ToLowerInvariant();

        if (text.StartsWith('/'))
        {
            await HandleCommandAsync(text, cancellation);
            return;
        }

        var repliedTo = message.ReplyToMessageId is { } replied ? book.FindByMessage(replied) : null;
        var target = repliedTo ?? book.LatestPending();
        if (Yes.Contains(word) || No.Contains(word))
        {
            if (message.ReplyToMessageId is not null && repliedTo is null)
            {
                // Answering a message that is not one of this run's alerts (an alert from before a restart, for example) must never fall
                // through to a different alert.
                await Say("That message is not an active alert (alerts do not survive a restart of the service). Reply to a current alert, or send yes / no on its own for the latest one.", cancellation);
                return;
            }

            if (target is null)
            {
                await Say("There is no alert waiting for an answer.", cancellation);
                return;
            }

            await Say(Yes.Contains(word) ? await ApproveAsync(target, cancellation) : await DismissAsync(target, cancellation), cancellation);
            return;
        }

        if (target is null)
        {
            await Say("There is no recent alert to talk about. " + Help, cancellation);
            return;
        }

        var answer = await analyst.AnswerAsync(target.Proposal, text, cancellation);
        await Say($"<b>#{target.Id}</b> {AlertFormatter.Escape(answer)}", cancellation);
    }

    private async Task HandleCommandAsync(string text, CancellationToken cancellation)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].Split('@')[0].ToLowerInvariant();
        switch (command)
        {
            case "/pending":
                var pending = book.Pending();
                await Say(pending.Count == 0
                    ? "Nothing is waiting."
                    : string.Join('\n', pending.Select(i => $"#{i.Id} {AlertFormatter.Escape(i.Proposal.Finding.Rule)} <code>{AlertFormatter.Escape(i.Proposal.Ip)}</code>")), cancellation);
                break;
            case "/blocks":
                var active = blocks.Active;
                await Say(active.Count == 0
                    ? "No address is blocked."
                    : string.Join('\n', active.Select(b => $"<code>{AlertFormatter.Escape(b.Ip)}</code> " + (b.ExpiresAt is { } at ? $"until {at:u}" : "permanently"))), cancellation);
                break;
            case "/unblock":
                await Say(await UnblockAsync(parts.Length > 1 ? parts[1] : "", cancellation), cancellation);
                break;
            default:
                await Say(Help, cancellation);
                break;
        }
    }

    private async Task<string> UnblockAsync(string ip, CancellationToken cancellation)
    {
        if (!IPAddress.TryParse(ip, out var address)) return "Usage: /unblock &lt;ip&gt;";
        try
        {
            return await blocks.UnblockAsync(address.ToString(), cancellation)
                ? $"Unblocked <code>{AlertFormatter.Escape(address.ToString())}</code>."
                : "That address is not blocked by this service.";
        }
        catch (FirewallException e)
        {
            return "Failed: " + AlertFormatter.Escape(e.Message);
        }
    }

    private async Task<string> ApproveAsync(Incident incident, CancellationToken cancellation)
    {
        if (incident.Status != IncidentStatus.Pending) return $"#{incident.Id} was already answered.";

        var p = incident.Proposal;
        if (p.Action != ActionKind.BlockIp || allowlist.IsProtected(p.Ip))
        {
            await Close(incident, IncidentStatus.Dismissed, cancellation);
            return $"#{incident.Id}: nothing to block.";
        }

        try
        {
            await blocks.BlockAsync(p.Ip, p.BlockFor, $"{p.Finding.Rule}: {p.Summary}", cancellation);
        }
        catch (FirewallException e)
        {
            log?.Write("block_failed", new { p.Ip, error = e.Message });
            return $"Could not block {AlertFormatter.Escape(p.Ip)}: {AlertFormatter.Escape(e.Message)}";
        }

        await Close(incident, IncidentStatus.Approved, cancellation);
        return $"✅ #{incident.Id}: blocked <code>{AlertFormatter.Escape(p.Ip)}</code> for {AlertFormatter.Duration(p.BlockFor)}.";
    }

    private async Task<string> DismissAsync(Incident incident, CancellationToken cancellation)
    {
        if (incident.Status != IncidentStatus.Pending) return $"#{incident.Id} was already answered.";

        await Close(incident, IncidentStatus.Dismissed, cancellation);
        return $"🚫 #{incident.Id}: ignored. {AlertFormatter.Escape(incident.Proposal.Ip)} will not be blocked.";
    }

    private async Task Close(Incident incident, IncidentStatus status, CancellationToken cancellation)
    {
        incident.Status = status;
        if (incident.MessageId is { } id) await ClearButtonsQuietlyAsync(chatId, id, cancellation);
    }

    private async Task ClearButtonsQuietlyAsync(long chat, long messageId, CancellationToken cancellation)
    {
        try
        {
            await telegram.ClearButtonsAsync(chat, messageId, cancellation);
        }
        catch (TelegramException e)
        {
            log?.Write("telegram_clear_failed", new { error = e.Message });
        }
    }

    private Task<long> Say(string html, CancellationToken cancellation) => telegram.SendMessageAsync(chatId, html, null, cancellation);
}
