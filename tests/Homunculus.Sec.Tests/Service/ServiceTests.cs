using System.Text.Json;
using Homunculus.Llm;
using Homunculus.Sec.Analysis;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Firewall;
using Homunculus.Sec.Logs;
using Homunculus.Sec.Safety;
using Homunculus.Sec.Service;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Tests.Service;

public class ServiceTests
{
    private const long Chat = 42;

    private string _dir = null!;
    private FakeTelegram _telegram = null!;
    private DryRunFirewall _firewall = null!;
    private BlockManager _blocks = null!;
    private IncidentBook _book = null!;
    private FakeModel _model = null!;
    private SecMonitor _monitor = null!;
    private ChatHandler _chat = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("homunculus-sec-").FullName;
        _telegram = new FakeTelegram();
        _firewall = new DryRunFirewall();
        _blocks = new BlockManager(_firewall, Path.Combine(_dir, "blocks.json"));
        _book = new IncidentBook();
        _model = new FakeModel();
        var allowlist = new Allowlist();
        var analyst = new Analyst(_model);
        _chat = new ChatHandler(_telegram, Chat, _book, _blocks, analyst, allowlist);
        _monitor = new SecMonitor([], [], analyst, new ProposalGuard(allowlist), _book, _telegram, Chat, _blocks, _chat);
    }

    [TearDown]
    public void TearDown()
    {
        _blocks.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private static Finding Finding(string ip = "203.0.113.5") => new(
        DefaultDetectors.WebScanner, ip, DateTimeOffset.UnixEpoch, 4, TimeSpan.FromMinutes(10), ["GET /.env <script>"]);

    private static Update Text(string text, long from = Chat, long chat = Chat, long? replyTo = null) =>
        new(1, new IncomingMessage(chat, from, 99, text, replyTo), null);

    private static Update Press(string data, long from = Chat) => new(1, null, new ButtonPress("p1", from, from, 7, data));

    private async Task<Incident> RaiseAlertAsync(string ip = "203.0.113.5")
    {
        _model.Verdict = """{"severity":"high","action":"block_ip","block_hours":12,"summary":"Scanning","reason":"Probes /.env"}""";
        await _monitor.OnFindingAsync(Finding(ip), CancellationToken.None);
        return _book.LatestPending()!;
    }

    [Test]
    public async Task AFindingBecomesAnAlertWithButtons()
    {
        var incident = await RaiseAlertAsync();

        var sent = _telegram.Sent.Single();
        Assert.That(sent.Html, Does.Contain("203.0.113.5").And.Contain("Scanning").And.Contain("12h"));
        Assert.That(sent.Buttons!.Select(b => b.Data), Is.EqualTo(new[] { $"a:{incident.Token}", $"i:{incident.Token}" }));
        Assert.That(incident.MessageId, Is.Not.Null);
    }

    [Test]
    public async Task NothingIsBlockedUntilTheOperatorApproves()
    {
        await RaiseAlertAsync();

        Assert.That(_firewall.Actions, Is.Empty);
        Assert.That(_blocks.IsBlocked("203.0.113.5"), Is.False);
    }

    [Test]
    public async Task PressingApproveBlocksTheAddress()
    {
        var incident = await RaiseAlertAsync();

        await _chat.HandleAsync(Press($"a:{incident.Token}"), CancellationToken.None);

        Assert.That(_firewall.Actions, Is.EqualTo(new[] { "block 203.0.113.5" }));
        Assert.That(incident.Status, Is.EqualTo(IncidentStatus.Approved));
        Assert.That(_telegram.Cleared, Is.EqualTo(new[] { incident.MessageId!.Value }));
        Assert.That(_blocks.Active.Single().ExpiresAt, Is.Not.Null);
    }

    [Test]
    public async Task PressingIgnoreBlocksNothing()
    {
        var incident = await RaiseAlertAsync();

        await _chat.HandleAsync(Press($"i:{incident.Token}"), CancellationToken.None);

        Assert.That(_firewall.Actions, Is.Empty);
        Assert.That(incident.Status, Is.EqualTo(IncidentStatus.Dismissed));
    }

    [Test]
    public async Task ARepliedYesApprovesTheIncidentRepliedTo()
    {
        var first = await RaiseAlertAsync("203.0.113.5");
        var second = await RaiseAlertAsync("203.0.113.6");

        await _chat.HandleAsync(Text("Yes!", replyTo: first.MessageId), CancellationToken.None);

        Assert.That(first.Status, Is.EqualTo(IncidentStatus.Approved));
        Assert.That(second.Status, Is.EqualTo(IncidentStatus.Pending));
    }

    [Test]
    public async Task AYesRepliedToAnOldMessageNeverApprovesADifferentAlert()
    {
        var current = await RaiseAlertAsync("203.0.113.5");

        // Replying to message 9999, which is no alert of this run (an alert from before a restart, say).
        await _chat.HandleAsync(Text("yes", replyTo: 9999), CancellationToken.None);

        Assert.That(current.Status, Is.EqualTo(IncidentStatus.Pending));
        Assert.That(_firewall.Actions, Is.Empty);
        Assert.That(_telegram.Sent.Last().Html, Does.Contain("not an active alert"));
    }

    [Test]
    public async Task AnUnrepliedYesApprovesTheLatestPendingIncident()
    {
        await RaiseAlertAsync("203.0.113.5");
        var latest = await RaiseAlertAsync("203.0.113.6");

        await _chat.HandleAsync(Text("yes"), CancellationToken.None);

        Assert.That(latest.Status, Is.EqualTo(IncidentStatus.Approved));
        Assert.That(_firewall.Actions, Is.EqualTo(new[] { "block 203.0.113.6" }));
    }

    [Test]
    public async Task AButtonFromBeforeARestartCannotActOnADifferentAlertThatReusedItsNumber()
    {
        // Alert #1 of the previous run, still on screen in Telegram: its button data is whatever the old process put in it.
        var previousRun = new IncidentBook();
        var oldAlert = previousRun.Add(BlockProposal("203.0.113.9"));

        // The service restarted: a new run, and its own alert #1 is about another address.
        var current = await RaiseAlertAsync("203.0.113.5");

        await _chat.HandleAsync(Press(AlertFormatter.Buttons(oldAlert)[0].Data), CancellationToken.None);

        Assert.That(current.Status, Is.EqualTo(IncidentStatus.Pending));
        Assert.That(_firewall.Actions, Is.Empty);
        Assert.That(_telegram.Answers.Last(), Does.Contain("restarted"));
    }

    [Test]
    public async Task AButtonForAnAlertTheServiceNoLongerKnowsIsExplainedAndRemoved()
    {
        var oldAlert = new IncidentBook().Add(BlockProposal("203.0.113.9"));
        var press = new Update(1, null, new ButtonPress("p1", Chat, Chat, 55, AlertFormatter.Buttons(oldAlert)[0].Data));

        await _chat.HandleAsync(press, CancellationToken.None);

        Assert.That(_telegram.Answers.Last(), Does.Contain("restarted"));
        Assert.That(_telegram.Cleared, Is.EqualTo(new long[] { 55 }));
    }

    private static Proposal BlockProposal(string ip) => new(
        Finding(ip), Severity.High, ActionKind.BlockIp, TimeSpan.FromHours(2), "s", "r");

    [Test]
    public async Task AnAlertCannotBeApprovedTwice()
    {
        var incident = await RaiseAlertAsync();

        await _chat.HandleAsync(Press($"a:{incident.Token}"), CancellationToken.None);
        await _chat.HandleAsync(Press($"a:{incident.Token}"), CancellationToken.None);

        Assert.That(_firewall.Actions, Has.Count.EqualTo(1));
        Assert.That(_telegram.Answers.Last(), Does.Contain("already"));
    }

    [Test]
    public async Task AnAddressThatIsAlreadyHandledGetsNoSecondAlert()
    {
        await RaiseAlertAsync();
        await _monitor.OnFindingAsync(Finding(), CancellationToken.None);

        Assert.That(_telegram.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ABlockedAddressGetsNoNewAlert()
    {
        await _blocks.BlockAsync("203.0.113.5", TimeSpan.FromHours(1), "x", CancellationToken.None);

        await _monitor.OnFindingAsync(Finding(), CancellationToken.None);

        Assert.That(_telegram.Sent, Is.Empty);
    }

    [Test]
    public async Task AnIgnoreVerdictSendsNoAlert()
    {
        _model.Verdict = """{"severity":"low","action":"ignore","summary":"Googlebot","reason":"Known crawler"}""";

        await _monitor.OnFindingAsync(Finding(), CancellationToken.None);

        Assert.That(_telegram.Sent, Is.Empty);
    }

    [Test]
    public async Task AProtectedAddressIsNeverOfferedForBlocking()
    {
        _model.Verdict = """{"severity":"high","action":"block_ip","summary":"s","reason":"r"}""";

        await _monitor.OnFindingAsync(Finding("192.168.1.10"), CancellationToken.None);

        Assert.That(_telegram.Sent, Is.Empty);
    }

    [Test]
    public async Task TextInTheAlertIsEscaped()
    {
        _model.Verdict = """{"severity":"high","action":"block_ip","summary":"<b>x</b> & y","reason":"r"}""";

        await _monitor.OnFindingAsync(Finding(), CancellationToken.None);

        Assert.That(_telegram.Sent.Single().Html, Does.Contain("&lt;b&gt;x&lt;/b&gt; &amp; y"));
    }

    [Test]
    public async Task MessagesFromAnyoneElseAreIgnored()
    {
        var incident = await RaiseAlertAsync();

        await _chat.HandleAsync(Text("yes", from: 666, chat: 666), CancellationToken.None);
        await _chat.HandleAsync(Press($"a:{incident.Token}", from: 666), CancellationToken.None);
        await _chat.HandleAsync(Text("yes", from: 666, chat: Chat), CancellationToken.None);

        Assert.That(incident.Status, Is.EqualTo(IncidentStatus.Pending));
        Assert.That(_firewall.Actions, Is.Empty);
        Assert.That(_telegram.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AQuestionIsAnsweredByTheModelAndChangesNothing()
    {
        var incident = await RaiseAlertAsync();
        _model.Answer = "It looks like an automated scanner.";

        await _chat.HandleAsync(Text("is this a real attack?"), CancellationToken.None);

        Assert.That(_telegram.Sent.Last().Html, Does.Contain("automated scanner"));
        Assert.That(incident.Status, Is.EqualTo(IncidentStatus.Pending));
        Assert.That(_model.LastToolCount, Is.Zero);
        Assert.That(_firewall.Actions, Is.Empty);
    }

    [Test]
    public async Task UnblockCommandRemovesABlock()
    {
        await _blocks.BlockAsync("203.0.113.5", TimeSpan.FromHours(1), "x", CancellationToken.None);

        await _chat.HandleAsync(Text("/unblock 203.0.113.5"), CancellationToken.None);

        Assert.That(_blocks.IsBlocked("203.0.113.5"), Is.False);
    }

    [Test]
    public async Task UnblockCommandRejectsGarbage()
    {
        await _chat.HandleAsync(Text("/unblock ; reboot"), CancellationToken.None);

        Assert.That(_telegram.Sent.Single().Html, Does.Contain("Usage"));
    }

    [Test]
    public async Task AFailedFirewallCommandLeavesTheAlertPending()
    {
        var incident = await RaiseAlertAsync();
        using var failing = new BlockManager(new ThrowingFirewall(), Path.Combine(_dir, "other.json"));
        var chat = new ChatHandler(_telegram, Chat, _book, failing, new Analyst(_model), new Allowlist());

        await chat.HandleAsync(Press($"a:{incident.Token}"), CancellationToken.None);

        Assert.That(incident.Status, Is.EqualTo(IncidentStatus.Pending));
        Assert.That(_telegram.Answers.Last(), Does.Contain("Could not block"));
    }

    private SecMonitor MonitorWith(Action<string> report, string? startup = null, TimeSpan? retry = null)
    {
        var allowlist = new Allowlist();
        var analyst = new Analyst(_model);
        WatchedLog[] logs = [new(new LogTailer(Path.Combine(_dir, "access.log"), TimeSpan.FromMilliseconds(20)), new NginxAccessParser())];
        return new SecMonitor(logs, [], analyst, new ProposalGuard(allowlist), _book, _telegram, Chat, _blocks, _chat, report: report, startupMessage: startup, retryDelay: retry);
    }

    [Test]
    public async Task SavedBlocksAreAppliedAgainWhenTheServiceStarts()
    {
        await _blocks.BlockAsync("203.0.113.5", TimeSpan.FromHours(5), "earlier run", CancellationToken.None);
        var firewall = new DryRunFirewall();
        using var restarted = new BlockManager(firewall, Path.Combine(_dir, "blocks.json"));
        var reports = new List<string>();
        var allowlist = new Allowlist();
        WatchedLog[] logs = [new(new LogTailer(Path.Combine(_dir, "access.log"), TimeSpan.FromMilliseconds(20)), new NginxAccessParser())];
        var monitor = new SecMonitor(logs, [], new Analyst(_model), new ProposalGuard(allowlist), _book, _telegram, Chat, restarted, _chat, report: reports.Add);
        _telegram.OnPoll = async cancellation =>
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            return [];
        };
        using var cancellation = new CancellationTokenSource();

        var run = monitor.RunAsync(cancellation.Token);
        await Task.Delay(150);
        await cancellation.CancelAsync();
        await run;

        Assert.That(firewall.Actions, Is.EqualTo(new[] { "block 203.0.113.5" }));
        Assert.That(reports, Does.Contain("Re-applied 1 saved block(s) to the firewall."));
    }

    [Test]
    public async Task TheServiceAnnouncesItselfInTheChatWhenItStarts()
    {
        var reports = new List<string>();
        var monitor = MonitorWith(reports.Add, "started");
        _telegram.OnPoll = async cancellation =>
        {
            await Task.Delay(Timeout.Infinite, cancellation);
            return [];
        };
        using var cancellation = new CancellationTokenSource();

        var run = monitor.RunAsync(cancellation.Token);
        await Task.Delay(100);
        await cancellation.CancelAsync();
        await run;

        Assert.That(_telegram.Sent.Select(m => m.Html), Is.EqualTo(new[] { "started" }));
        Assert.That(reports, Does.Contain("Startup message sent to Telegram."));
    }

    [Test]
    public void TheServiceStopsWithTheErrorWhenOneOfItsLoopsDies()
    {
        var monitor = MonitorWith(_ => { });
        _telegram.OnPoll = _ => throw new NotSupportedException("boom");

        var e = Assert.ThrowsAsync<NotSupportedException>(() => monitor.RunAsync(CancellationToken.None));

        Assert.That(e!.Message, Is.EqualTo("boom"));
    }

    [Test]
    public async Task ATelegramOutageIsReportedOnceAndPollingGoesOn()
    {
        var reports = new List<string>();
        var monitor = MonitorWith(reports.Add, retry: TimeSpan.FromMilliseconds(30));
        var calls = 0;
        _telegram.OnPoll = async cancellation =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new TelegramException("Telegram getUpdates failed: HttpRequestException.");
            await Task.Delay(Timeout.Infinite, cancellation);
            return [];
        };
        using var cancellation = new CancellationTokenSource();

        var run = monitor.RunAsync(cancellation.Token);
        await Task.Delay(500);
        await cancellation.CancelAsync();
        await run;

        Assert.That(reports.Count(r => r.Contains("polling failed", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(calls, Is.GreaterThanOrEqualTo(2));
    }

    private sealed class ThrowingFirewall : IFirewall
    {
        public Task BlockAsync(string ip, CancellationToken cancellation) => throw new FirewallException("sudo: a password is required");

        public Task UnblockAsync(string ip, CancellationToken cancellation) => Task.CompletedTask;
    }

    private sealed class FakeModel : ILanguageModel
    {
        public string Verdict { get; set; } = "";
        public string Answer { get; set; } = "";
        public int LastToolCount { get; private set; } = -1;

        public Task<Reply> CompleteAsync(IConversation conversation, IReadOnlyList<ToolSpec> tools, CancellationToken cancellation)
        {
            LastToolCount = tools.Count;
            return Task.FromResult(tools.Count == 0
                ? new Reply(Message.Assistant(Answer))
                : new Reply(Message.Assistant(null, [new ToolCall("1", Analyst.ToolName, JsonDocument.Parse(Verdict).RootElement)])));
        }
    }

    private sealed class FakeTelegram : ITelegramApi
    {
        private long _next = 100;

        public List<(string Html, IReadOnlyList<Button>? Buttons)> Sent { get; } = [];
        public List<long> Cleared { get; } = [];
        public List<string?> Answers { get; } = [];

        public Func<CancellationToken, Task<IReadOnlyList<Update>>>? OnPoll { get; set; }

        public Task<IReadOnlyList<Update>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellation) =>
            OnPoll?.Invoke(cancellation) ?? Task.FromResult<IReadOnlyList<Update>>([]);

        public Task<long> SendMessageAsync(long chatId, string html, IReadOnlyList<Button>? buttons, CancellationToken cancellation)
        {
            Sent.Add((html, buttons));
            Answers.Add(html);
            return Task.FromResult(_next++);
        }

        public Task AnswerPressAsync(string pressId, string? text, CancellationToken cancellation)
        {
            Answers.Add(text);
            return Task.CompletedTask;
        }

        public Task ClearButtonsAsync(long chatId, long messageId, CancellationToken cancellation)
        {
            Cleared.Add(messageId);
            return Task.CompletedTask;
        }
    }
}
