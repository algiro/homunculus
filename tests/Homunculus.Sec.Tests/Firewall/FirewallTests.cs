using Homunculus.Sec.Firewall;

namespace Homunculus.Sec.Tests.Firewall;

public class FirewallTests
{
    private string _dir = null!;
    private string _state = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("homunculus-sec-").FullName;
        _state = Path.Combine(_dir, "state", "blocks.json");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [Test]
    public async Task Ufw_RunsOnlyTheWrapperThroughSudoWithoutAShell()
    {
        var runner = new FakeRunner();

        await new UfwFirewall(runner).BlockAsync("203.0.113.5", CancellationToken.None);

        Assert.That(runner.Calls.Single(), Is.EqualTo("sudo -n /usr/local/sbin/homunculus-sec-ufw block 203.0.113.5"));
    }

    [Test]
    public void Ufw_ReportsAFailure()
    {
        var runner = new FakeRunner((_, _) => new ProcessResult(1, "sudo: a password is required"));

        var e = Assert.ThrowsAsync<FirewallException>(() => new UfwFirewall(runner).BlockAsync("203.0.113.5", CancellationToken.None));

        Assert.That(e!.Message, Does.Contain("password is required"));
    }

    [TestCase("1.2.3.4; rm -rf /")]
    [TestCase("--help")]
    [TestCase("$(reboot)")]
    [TestCase("")]
    public async Task Ufw_RefusesAnythingThatIsNotAnIpAddress(string ip)
    {
        var runner = new FakeRunner();

        Assert.ThrowsAsync<FirewallException>(() => new UfwFirewall(runner).BlockAsync(ip, CancellationToken.None));
        Assert.ThrowsAsync<FirewallException>(() => new UfwFirewall(runner).UnblockAsync(ip, CancellationToken.None));
        await Task.CompletedTask;

        Assert.That(runner.Calls, Is.Empty);
    }

    [Test]
    public async Task Ufw_UnblockingAMissingRuleIsNotAnError()
    {
        var runner = new FakeRunner((_, _) => new ProcessResult(1, "Could not delete non-existent rule"));

        await new UfwFirewall(runner).UnblockAsync("203.0.113.5", CancellationToken.None);

        Assert.That(runner.Calls.Single(), Is.EqualTo("sudo -n /usr/local/sbin/homunculus-sec-ufw unblock 203.0.113.5"));
    }

    [Test]
    public async Task Manager_BlocksRecordsAndUnblocks()
    {
        var firewall = new DryRunFirewall();
        using var manager = new BlockManager(firewall, _state);

        await manager.BlockAsync("203.0.113.5", TimeSpan.FromHours(1), "test", CancellationToken.None);

        Assert.That(manager.IsBlocked("203.0.113.5"), Is.True);
        Assert.That(File.Exists(_state), Is.True);
        Assert.That(await manager.UnblockAsync("203.0.113.5", CancellationToken.None), Is.True);
        Assert.That(manager.IsBlocked("203.0.113.5"), Is.False);
        Assert.That(await manager.UnblockAsync("203.0.113.5", CancellationToken.None), Is.False);
        Assert.That(firewall.Actions, Is.EqualTo(new[] { "block 203.0.113.5", "unblock 203.0.113.5" }));
    }

    [Test]
    public async Task Manager_RecordsNothingWhenTheFirewallRefuses()
    {
        using var manager = new BlockManager(new FailingFirewall(), _state);

        Assert.ThrowsAsync<FirewallException>(() => manager.BlockAsync("203.0.113.5", TimeSpan.FromHours(1), "test", CancellationToken.None));
        await Task.CompletedTask;

        Assert.That(manager.IsBlocked("203.0.113.5"), Is.False);
    }

    [Test]
    public async Task Manager_ExpiresDueBlocksAndKeepsTheRest()
    {
        var clock = new FakeClock();
        var firewall = new DryRunFirewall();
        using var manager = new BlockManager(firewall, _state, clock);
        await manager.BlockAsync("203.0.113.1", TimeSpan.FromHours(1), "short", CancellationToken.None);
        await manager.BlockAsync("203.0.113.2", TimeSpan.FromHours(5), "long", CancellationToken.None);
        await manager.BlockAsync("203.0.113.3", null, "forever", CancellationToken.None);

        clock.Advance(TimeSpan.FromHours(2));
        var expired = await manager.ExpireDueAsync(CancellationToken.None);

        Assert.That(expired, Is.EqualTo(new[] { "203.0.113.1" }));
        Assert.That(manager.Active.Select(b => b.Ip), Is.EquivalentTo(new[] { "203.0.113.2", "203.0.113.3" }));
    }

    [Test]
    public async Task Manager_KeepsABlockWhoseUnblockFailsAndRetriesLater()
    {
        var clock = new FakeClock();
        var firewall = new FailingFirewall { FailBlock = false, FailUnblock = false };
        using var manager = new BlockManager(firewall, _state, clock);
        await manager.BlockAsync("203.0.113.1", TimeSpan.FromHours(1), "x", CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(2));

        firewall.FailUnblock = true;
        Assert.That(await manager.ExpireDueAsync(CancellationToken.None), Is.Empty);
        Assert.That(manager.IsBlocked("203.0.113.1"), Is.True);

        firewall.FailUnblock = false;
        Assert.That(await manager.ExpireDueAsync(CancellationToken.None), Is.EqualTo(new[] { "203.0.113.1" }));
    }

    [Test]
    public async Task Manager_BlockingAgainExtendsAndNeverShortensOrRepeatsTheFirewallCall()
    {
        var clock = new FakeClock();
        var firewall = new DryRunFirewall();
        using var manager = new BlockManager(firewall, _state, clock);

        await manager.BlockAsync("203.0.113.1", TimeSpan.FromHours(10), "a", CancellationToken.None);
        await manager.BlockAsync("203.0.113.1", TimeSpan.FromHours(1), "b", CancellationToken.None);
        await manager.BlockAsync("203.0.113.2", TimeSpan.FromHours(1), "a", CancellationToken.None);
        await manager.BlockAsync("203.0.113.2", null, "b", CancellationToken.None);

        Assert.That(firewall.Actions, Has.Count.EqualTo(2));
        Assert.That(manager.Active.Single(b => b.Ip == "203.0.113.1").ExpiresAt, Is.EqualTo(clock.GetUtcNow() + TimeSpan.FromHours(10)));
        Assert.That(manager.Active.Single(b => b.Ip == "203.0.113.2").ExpiresAt, Is.Null);
    }

    [Test]
    public async Task Manager_RemembersBlocksAcrossARestart()
    {
        var clock = new FakeClock();
        using (var first = new BlockManager(new DryRunFirewall(), _state, clock))
            await first.BlockAsync("203.0.113.1", TimeSpan.FromHours(1), "x", CancellationToken.None);

        var firewall = new DryRunFirewall();
        using var second = new BlockManager(firewall, _state, clock);
        clock.Advance(TimeSpan.FromHours(2));
        await second.ExpireDueAsync(CancellationToken.None);

        Assert.That(firewall.Actions, Is.EqualTo(new[] { "unblock 203.0.113.1" }));
    }

    [Test]
    public void Manager_StartsEmptyWhenTheStateFileIsCorrupt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_state)!);
        File.WriteAllText(_state, "{ not json");

        using var manager = new BlockManager(new DryRunFirewall(), _state);

        Assert.That(manager.Active, Is.Empty);
    }

    private sealed class FakeRunner(Func<string, int, ProcessResult>? respond = null) : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellation)
        {
            var line = string.Join(' ', [file, .. arguments]);
            Calls.Add(line);
            return Task.FromResult(respond?.Invoke(line, Calls.Count) ?? new ProcessResult(0, ""));
        }
    }

    private sealed class FailingFirewall : IFirewall
    {
        public bool FailBlock { get; set; } = true;

        public bool FailUnblock { get; set; } = true;

        public Task BlockAsync(string ip, CancellationToken cancellation) =>
            FailBlock ? throw new FirewallException("no") : Task.CompletedTask;

        public Task UnblockAsync(string ip, CancellationToken cancellation) =>
            FailUnblock ? throw new FirewallException("no") : Task.CompletedTask;
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan span) => _now += span;
    }
}
