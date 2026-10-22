using System.Text.Json;

namespace Homunculus.Logging;

/// <summary>
/// Appends one JSON object per line: <c>{"time":…,"event":…,"data":…}</c>. A failing disk never breaks the agent,
/// so write errors are swallowed. The file holds prompts and file contents, so it must stay out of version control.
/// </summary>
public sealed class JsonlEventLog : IEventLog, IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private readonly Func<DateTimeOffset> _now;

    public JsonlEventLog(string path, Func<DateTimeOffset>? now = null)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _writer = new StreamWriter(new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public void Write(string eventName, object? data = null)
    {
        try
        {
            var line = JsonSerializer.Serialize(new { time = _now(), @event = eventName, data }, Options);
            lock (_gate) _writer.WriteLine(line);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or NotSupportedException or JsonException)
        {
        }
    }

    public void Dispose()
    {
        lock (_gate) _writer.Dispose();
    }

    /// <summary>A server's body as JSON when it is JSON, so the log stays one parseable line; otherwise the raw text.</summary>
    public static object Raw(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (JsonException)
        {
            return body;
        }
    }

    public static string Truncate(string text, int max = 4000) =>
        text.Length <= max ? text : $"{text[..max]}… [{text.Length - max} more chars]";
}
