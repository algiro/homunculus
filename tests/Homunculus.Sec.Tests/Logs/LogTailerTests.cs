using Homunculus.Sec.Detection;
using Homunculus.Sec.Logs;

namespace Homunculus.Sec.Tests.Logs;

public class LogTailerTests
{
    private string _dir = null!;
    private string _file = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("homunculus-sec-").FullName;
        _file = Path.Combine(_dir, "access.log");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [Test]
    public void StartsAtTheEndOfTheFile()
    {
        File.WriteAllText(_file, "old\n");
        var tailer = new LogTailer(_file);

        Assert.That(tailer.ReadAvailable().Lines, Is.Empty);
        File.AppendAllText(_file, "new\n");

        Assert.That(tailer.ReadAvailable().Lines, Is.EqualTo(new[] { "new" }));
    }

    [Test]
    public void FromStart_ReadsExistingLines()
    {
        File.WriteAllText(_file, "one\r\ntwo\n");

        Assert.That(new LogTailer(_file, fromStart: true).ReadAvailable().Lines, Is.EqualTo(new[] { "one", "two" }));
    }

    [Test]
    public void HoldsBackAHalfWrittenLineUntilItIsComplete()
    {
        File.WriteAllText(_file, "");
        var tailer = new LogTailer(_file);
        tailer.ReadAvailable();

        File.AppendAllText(_file, "half");
        Assert.That(tailer.ReadAvailable().Lines, Is.Empty);
        File.AppendAllText(_file, "-done\n");

        Assert.That(tailer.ReadAvailable().Lines, Is.EqualTo(new[] { "half-done" }));
    }

    [Test]
    public void StartsAgainWhenTheFileIsTruncated()
    {
        File.WriteAllText(_file, "a long first line\n");
        var tailer = new LogTailer(_file, fromStart: true);
        tailer.ReadAvailable();

        File.WriteAllText(_file, "x\n");

        Assert.That(tailer.ReadAvailable().Lines, Is.EqualTo(new[] { "x" }));
    }

    [Test]
    public void WaitsForAMissingFileAndThenReadsItFromTheTop()
    {
        var tailer = new LogTailer(_file);

        Assert.That(tailer.ReadAvailable().Lines, Is.Empty);
        File.WriteAllText(_file, "first\n");

        Assert.That(tailer.ReadAvailable().Lines, Is.EqualTo(new[] { "first" }));
    }

    [Test]
    public void ReadsMultiByteCharactersSplitAcrossReads()
    {
        File.WriteAllBytes(_file, []);
        var tailer = new LogTailer(_file);
        tailer.ReadAvailable();
        var bytes = System.Text.Encoding.UTF8.GetBytes("café\n");

        using (var stream = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            stream.Write(bytes, 0, 4);
        Assert.That(tailer.ReadAvailable().Lines, Is.Empty);
        using (var stream = new FileStream(_file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            stream.Write(bytes, 4, bytes.Length - 4);

        Assert.That(tailer.ReadAvailable().Lines, Is.EqualTo(new[] { "café" }));
    }

    [Test]
    public async Task Watcher_TurnsAppendedAttackLinesIntoAFinding()
    {
        File.WriteAllText(_file, "");
        var found = new TaskCompletionSource<Finding>();
        var watcher = new LogWatcher(
            [new WatchedLog(new LogTailer(_file, TimeSpan.FromMilliseconds(20), fromStart: true), new NginxAccessParser())],
            DefaultDetectors.Create(new DetectorOptions { ProbeCount = 2 }),
            finding =>
            {
                found.TrySetResult(finding);
                return Task.CompletedTask;
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = watcher.RunAsync(cancellation.Token);

        File.AppendAllText(_file,
            """
            203.0.113.9 - - [19/Nov/2026:10:15:30 +0000] "GET /.env HTTP/1.1" 404 153 "-" "curl/8"
            203.0.113.9 - - [19/Nov/2026:10:15:31 +0000] "GET /wp-login.php HTTP/1.1" 404 153 "-" "curl/8"

            """);

        var finding = await found.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await run;

        Assert.That(finding.Rule, Is.EqualTo(DefaultDetectors.ScannerProbe));
        Assert.That(finding.Ip, Is.EqualTo("203.0.113.9"));
    }
}
