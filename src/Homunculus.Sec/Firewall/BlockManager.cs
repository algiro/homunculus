using System.Text.Json;
using Homunculus.Logging;

namespace Homunculus.Sec.Firewall;

/// <summary>An address the service has blocked. <paramref name="ExpiresAt"/> is null for a permanent block.</summary>
public sealed record ActiveBlock(string Ip, DateTimeOffset BlockedAt, DateTimeOffset? ExpiresAt, string Reason);

/// <summary>
/// Applies blocks through the firewall and remembers them in a state file, so a block still expires after the service restarts.
/// A block is recorded only after the firewall accepted it; an expiry that fails is retried on the next sweep.
/// </summary>
public sealed class BlockManager : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IFirewall _firewall;
    private readonly string _statePath;
    private readonly TimeProvider _clock;
    private readonly IEventLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ActiveBlock> _blocks = [];

    public BlockManager(IFirewall firewall, string statePath, TimeProvider? clock = null, IEventLog? log = null)
    {
        _firewall = firewall;
        _statePath = statePath;
        _clock = clock ?? TimeProvider.System;
        _log = log ?? NullEventLog.Instance;
        Load();
    }

    public void Dispose() => _gate.Dispose();

    public IReadOnlyList<ActiveBlock> Active
    {
        get
        {
            lock (_blocks) return _blocks.Values.OrderBy(b => b.BlockedAt).ToList();
        }
    }

    public bool IsBlocked(string ip)
    {
        lock (_blocks) return _blocks.ContainsKey(ip);
    }

    /// <summary>Blocks the address for <paramref name="duration"/> (null: for good). Blocking an address that is already blocked only extends it.</summary>
    /// <exception cref="FirewallException">The firewall refused.</exception>
    public async Task BlockAsync(string ip, TimeSpan? duration, string reason, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            var now = _clock.GetUtcNow();
            var expires = duration is null ? (DateTimeOffset?)null : now + duration.Value;
            ActiveBlock? existing;
            lock (_blocks) _blocks.TryGetValue(ip, out existing);

            if (existing is null) await _firewall.BlockAsync(ip, cancellation);
            else expires = Later(existing.ExpiresAt, expires);

            lock (_blocks) _blocks[ip] = new ActiveBlock(ip, existing?.BlockedAt ?? now, expires, reason);
            Save();
            _log.Write("blocked", new { ip, expires, reason });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes the block. Returns false when the address was not blocked by this service.</summary>
    /// <exception cref="FirewallException">The firewall could not remove it.</exception>
    public async Task<bool> UnblockAsync(string ip, CancellationToken cancellation)
    {
        await _gate.WaitAsync(cancellation);
        try
        {
            lock (_blocks)
                if (!_blocks.ContainsKey(ip)) return false;

            await _firewall.UnblockAsync(ip, cancellation);
            lock (_blocks) _blocks.Remove(ip);
            Save();
            _log.Write("unblocked", new { ip });
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies the blocks that are still running to the firewall again. Needed because some firewalls forget their rules when the
    /// machine restarts (plain iptables does), and harmless when they do not (blocking is idempotent). Blocks whose time is up are
    /// left to <see cref="ExpireDueAsync"/>. Returns how many were applied and how many the firewall refused.
    /// </summary>
    public async Task<(int Applied, int Failed)> RestoreAsync(CancellationToken cancellation, Func<string, bool>? isProtected = null)
    {
        var now = _clock.GetUtcNow();
        var running = Active.Where(b => b.ExpiresAt is null || b.ExpiresAt > now).Select(b => b.Ip).ToList();
        int applied = 0, failed = 0;

        await _gate.WaitAsync(cancellation);
        try
        {
            foreach (var ip in running)
            {
                if (isProtected?.Invoke(ip) == true)
                {
                    // Such an address should never have been blocked (the never-block list may have grown since): do not apply it.
                    failed++;
                    _log.Write("restore_skipped_protected", new { ip });
                    continue;
                }

                try
                {
                    await _firewall.BlockAsync(ip, cancellation);
                    applied++;
                }
                catch (FirewallException e)
                {
                    failed++;
                    _log.Write("restore_failed", new { ip, error = e.Message });
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        return (applied, failed);
    }

    /// <summary>Removes every block whose time is up. Returns the addresses that were unblocked.</summary>
    public async Task<IReadOnlyList<string>> ExpireDueAsync(CancellationToken cancellation)
    {
        var now = _clock.GetUtcNow();
        List<string> due;
        lock (_blocks) due = _blocks.Values.Where(b => b.ExpiresAt is { } at && at <= now).Select(b => b.Ip).ToList();

        var removed = new List<string>();
        foreach (var ip in due)
        {
            try
            {
                if (await UnblockAsync(ip, cancellation)) removed.Add(ip);
            }
            catch (FirewallException e)
            {
                _log.Write("unblock_failed", new { ip, error = e.Message });
            }
        }

        return removed;
    }

    /// <summary>A permanent block outlasts any timed one.</summary>
    private static DateTimeOffset? Later(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null || second is null ? null : first > second ? first : second;

    private void Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var blocks = JsonSerializer.Deserialize<List<ActiveBlock>>(File.ReadAllText(_statePath)) ?? [];
            foreach (var block in blocks) _blocks[block.Ip] = block;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Write("state_load_failed", new { error = e.Message });
        }
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_blocks) json = JsonSerializer.Serialize(_blocks.Values.ToList(), Json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_statePath))!);
            var temp = _statePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _statePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Write("state_save_failed", new { error = e.Message });
        }
    }
}
