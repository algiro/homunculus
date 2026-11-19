using System.Threading.Channels;
using Homunculus.Sec.Detection;

namespace Homunculus.Sec.Logs;

/// <summary>A log file to follow and the parser for its lines.</summary>
public sealed record WatchedLog(LogTailer Tailer, ILogParser Parser);

/// <summary>
/// Follows several logs at once, parses every new line and feeds it to the detectors. All entries pass through one channel, so the
/// detectors and the callback are only ever used from a single task.
/// </summary>
public sealed class LogWatcher(IReadOnlyList<WatchedLog> logs, IReadOnlyList<IDetector> detectors, Func<Finding, Task> onFinding)
{
    public async Task RunAsync(CancellationToken cancellation)
    {
        var channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(10_000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        var producers = logs.Select(log => Task.Run(() => PumpAsync(log, channel.Writer, cancellation), CancellationToken.None)).ToArray();
        var finished = Task.WhenAll(producers).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                foreach (var detector in detectors)
                    if (detector.Observe(entry) is { } finding)
                        await onFinding(finding);
            }
        }
        finally
        {
            await finished;
        }
    }

    private static async Task PumpAsync(WatchedLog log, ChannelWriter<LogEntry> writer, CancellationToken cancellation)
    {
        try
        {
            await foreach (var line in log.Tailer.ReadLinesAsync(cancellation))
                if (log.Parser.Parse(line) is { } entry)
                    await writer.WriteAsync(entry, cancellation);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
