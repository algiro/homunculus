using Homunculus.Logging;
using Homunculus.Sec.Analysis;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Firewall;
using Homunculus.Sec.Logs;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Service;

/// <summary>
/// The service's main loop. It runs three things side by side: the log watcher (finding to analysis to alert), the Telegram
/// poller (the operator's answers), and the sweep that removes blocks whose time is up.
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
    TimeSpan? sweepEvery = null)
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _sweepEvery = sweepEvery ?? TimeSpan.FromMinutes(1);

    public Task RunAsync(CancellationToken cancellation)
    {
        var watcher = new LogWatcher(logs, detectors, finding => OnFindingAsync(finding, cancellation));
        return Task.WhenAll(watcher.RunAsync(cancellation), PollAsync(cancellation), SweepAsync(cancellation));
    }

    /// <summary>Turns one finding into an alert: skips addresses already handled, asks the analyst, and sends the proposal.</summary>
    public async Task OnFindingAsync(Finding finding, CancellationToken cancellation)
    {
        log?.Write("finding", new { finding.Rule, finding.Ip, finding.Count });
        if (blocks.IsBlocked(finding.Ip) || book.HasPendingFor(finding.Ip)) return;

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
        }
    }

    private async Task PollAsync(CancellationToken cancellation)
    {
        long offset = 0;
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                foreach (var update in await telegram.GetUpdatesAsync(offset, 30, cancellation))
                {
                    offset = update.Id + 1;
                    try
                    {
                        await chat.HandleAsync(update, cancellation);
                    }
                    catch (Exception e) when (e is TelegramException or FirewallException)
                    {
                        log?.Write("update_failed", new { update.Id, error = e.Message });
                    }
                }
            }
            catch (TelegramException e)
            {
                log?.Write("poll_failed", new { error = e.Message });
                if (!await Delay(RetryDelay, cancellation)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
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
