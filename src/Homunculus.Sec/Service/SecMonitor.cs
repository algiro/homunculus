using Homunculus.Logging;
using Homunculus.Sec.Analysis;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Logs;
using Homunculus.Sec.Telegram;
using Homunculus.Sec.Firewall;

namespace Homunculus.Sec.Service;

/// <summary>
/// The service's main loop. It runs three things side by side: the log watcher (finding to analysis to alert), the Telegram
/// poller (the operator's answers), and the sweep that removes blocks whose time is up. If any of the three dies the whole
/// service stops with the error, so the supervisor restarts it and the failure is visible, instead of one half running on silently.
/// A single bad update or finding is reported and skipped; it never stops a loop.
/// </summary>
public sealed class SecMonitor(
    IReadOnlyList<WatchedLog> logs,
    IReadOnlyList<IDetector> detectors,
    Analyst analyst,
    ProposalGuard guard,
    IncidentBook book,
    ITelegramApi telegram,
    long chatId,
    BlockManager blocks,
    ChatHandler chat,
    IEventLog? log = null,
    TimeSpan? sweepEvery = null,
    Action<string>? report = null,
    string? startupMessage = null,
    TimeSpan? retryDelay = null)
{
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _sweepEvery = sweepEvery ?? TimeSpan.FromMinutes(1);

    /// <summary>Runs until cancelled. Throws the error of whichever loop failed first.</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var watcher = new LogWatcher(logs, detectors, finding => OnFindingSafelyAsync(finding, stop.Token));

        await RestoreBlocksAsync(stop.Token);

        // The greeting runs alongside the loops: a slow or unreachable Telegram must not hold the watcher back.
        var announcement = AnnounceAsync(stop.Token);
        Task[] loops = [watcher.RunAsync(stop.Token), PollAsync(stop.Token), SweepAsync(stop.Token)];

        var first = await Task.WhenAny(loops);
        await stop.CancelAsync();
        try
        {
            await Task.WhenAll(loops);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        await announcement;
        await first; // rethrows the failure that ended the service, if there was one
        if (!cancellation.IsCancellationRequested)
            throw new InvalidOperationException("A service loop ended without being asked to stop.");
    }

    /// <summary>Turns one finding into an alert: skips addresses already handled, asks the analyst, and sends the proposal.</summary>
    public async Task OnFindingAsync(Finding finding, CancellationToken cancellation)
    {
        log?.Write("finding", new { finding.Rule, finding.Ip, finding.Count });
        if (blocks.IsCovered(finding.Ip) || book.HasPendingCovering(finding.Ip)) return;

        var proposal = guard.Apply(await analyst.AnalyzeAsync(finding, cancellation));
        if (proposal.Action == ActionKind.Ignore)
        {
            log?.Write("finding_ignored", new { finding.Rule, finding.Ip, proposal.Reason });
            return;
        }

        var incident = book.Add(proposal);
        try
        {
            incident.MessageId = await telegram.SendMessageAsync(chatId, AlertFormatter.Alert(incident), AlertFormatter.Buttons(incident), cancellation);
        }
        catch (TelegramException e)
        {
            log?.Write("alert_failed", new { incident.Id, error = e.Message });
            report?.Invoke($"Could not send the alert for {finding.Ip}: {e.Message}");
        }
    }

    private async Task OnFindingSafelyAsync(Finding finding, CancellationToken cancellation)
    {
        try
        {
            await OnFindingAsync(finding, cancellation);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log?.Write("finding_failed", new { finding.Rule, finding.Ip, error = e.Message });
            report?.Invoke($"Could not handle the finding for {finding.Ip} ({finding.Rule}): {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Puts the saved blocks back into the firewall (it may have forgotten them in a reboot) and says so if it had to.</summary>
    private async Task RestoreBlocksAsync(CancellationToken cancellation)
    {
        var (applied, failed) = await blocks.RestoreAsync(cancellation, guard.IsProtected);
        if (applied + failed > 0) report?.Invoke($"Re-applied {applied} saved block(s) to the firewall{(failed > 0 ? $"; {failed} could not be applied" : "")}.");
    }

    /// <summary>Says hello in the chat. It is also the first proof that the token and the chat id are right.</summary>
    private async Task AnnounceAsync(CancellationToken cancellation)
    {
        if (startupMessage is null) return;
        try
        {
            await telegram.SendMessageAsync(chatId, startupMessage, null, cancellation);
            report?.Invoke("Startup message sent to Telegram.");
        }
        catch (TelegramException e)
        {
            report?.Invoke($"Could not send the startup message to Telegram (is the chat id right, and did you press Start in the bot?): {e.Message}");
        }
        catch (OperationCanceledException)
        {
            // Shut down before the greeting went out.
        }
    }

    private async Task PollAsync(CancellationToken cancellation)
    {
        long offset = 0;
        string? lastFailure = null;
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                foreach (var update in await telegram.GetUpdatesAsync(offset, 30, cancellation))
                {
                    offset = update.Id + 1;
                    await HandleSafelyAsync(update, cancellation);
                }

                if (lastFailure is not null) report?.Invoke("Telegram polling recovered.");
                lastFailure = null;
            }
            catch (TelegramException e)
            {
                log?.Write("poll_failed", new { error = e.Message });
                if (e.Message != lastFailure) report?.Invoke($"Telegram polling failed, retrying: {e.Message}");
                lastFailure = e.Message;
                if (!await Delay(_retryDelay, cancellation)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task HandleSafelyAsync(Update update, CancellationToken cancellation)
    {
        try
        {
            await chat.HandleAsync(update, cancellation);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log?.Write("update_failed", new { update.Id, error = e.Message });
            report?.Invoke($"Could not handle Telegram update {update.Id}: {e.GetType().Name}: {e.Message}");
        }
    }

    private async Task SweepAsync(CancellationToken cancellation)
    {
        while (await Delay(_sweepEvery, cancellation))
        {
            foreach (var ip in await blocks.ExpireDueAsync(cancellation))
                log?.Write("block_expired", new { ip });
        }
    }

    private static async Task<bool> Delay(TimeSpan time, CancellationToken cancellation)
    {
        try
        {
            await Task.Delay(time, cancellation);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
