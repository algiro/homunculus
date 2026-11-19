using System.Runtime.CompilerServices;
using System.Text;

namespace Homunculus.Sec.Logs;

/// <summary>
/// Follows a text file like <c>tail -f</c>: yields each complete line that is appended. Survives the file missing, being truncated,
/// or being replaced by logrotate (it is reopened by path on every poll, and a shorter file starts again from the top).
/// </summary>
public sealed class LogTailer(string path, TimeSpan? pollInterval = null, bool fromStart = false)
{
    private const int ChunkBytes = 64 * 1024;
    private const int MaxBytesPerPoll = 4 * 1024 * 1024;
    private const int MaxLineChars = 64 * 1024;

    private readonly TimeSpan _poll = pollInterval ?? TimeSpan.FromMilliseconds(500);
    private readonly StringBuilder _partial = new();
    private Decoder _decoder = Encoding.UTF8.GetDecoder();
    private long _position = -1;

    public string Path => path;

    public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            var (lines, more) = ReadAvailable();
            foreach (var line in lines) yield return line;
            if (more) continue;

            try
            {
                await Task.Delay(_poll, cancellation);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }

    /// <summary>Reads what was appended since the last call. <c>More</c> is true when the per-poll cap stopped it early.</summary>
    internal (List<string> Lines, bool More) ReadAvailable()
    {
        var lines = new List<string>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (_position < 0) _position = fromStart ? 0 : stream.Length;
            if (stream.Length < _position) Reset();

            stream.Seek(_position, SeekOrigin.Begin);
            var bytes = new byte[ChunkBytes];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(ChunkBytes)];
            var total = 0;
            while (total < MaxBytesPerPoll)
            {
                var read = stream.Read(bytes, 0, bytes.Length);
                if (read == 0) break;
                total += read;
                var count = _decoder.GetChars(bytes, 0, read, chars, 0);
                Split(chars.AsSpan(0, count), lines);
            }

            _position = stream.Position;
            return (lines, total >= MaxBytesPerPoll);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            // Mid-rotation: the next file starts from its beginning.
            Reset();
            return (lines, false);
        }
        catch (IOException)
        {
            return (lines, false);
        }
    }

    private void Reset()
    {
        _position = 0;
        _partial.Clear();
        _decoder = Encoding.UTF8.GetDecoder();
    }

    private void Split(ReadOnlySpan<char> text, List<string> lines)
    {
        foreach (var c in text)
        {
            if (c == '\n')
            {
                lines.Add(_partial.ToString().TrimEnd('\r'));
                _partial.Clear();
            }
            else if (_partial.Length < MaxLineChars)
            {
                _partial.Append(c);
            }
        }
    }
}
