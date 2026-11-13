using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homunculus.Tools;

/// <summary>
/// Reads one web page as plain text, shaped like Claude Code's WebFetch tool. The text is cut short because a small
/// model's context window is easily overrun by a whole page.
/// </summary>
public sealed class WebFetchTool(HttpClient http) : ITool
{
    public const string ToolName = "web_fetch";

    private const int MaxDownloadBytes = 2_000_000;

    /// <summary>A page this large that leaves this little text is drawn by scripts the tool does not run.</summary>
    private const int ScriptedPageBytes = 20_000;
    private const int ScriptedPageChars = 1_000;

    /// <summary>Ends every dead end, so that a small model moves on to the next result instead of giving up.</summary>
    private const string TryAnother = "Try another link from the search results.";

    public string Name => ToolName;

    public string Description =>
        $"Read a web page as plain text (the first {Limits.WebPageChars} characters). Use it on a link from web_search, or a link the user gave you. " +
        "Use it instead of curl: it removes scripts, menus and markup. Use the 'start' argument to read further into a long page.";

    public JsonObject Parameters => Schema.Of(
        Parameter.Required("url", "Full address starting with http:// or https://."),
        Parameter.Optional("start", $"Character to start reading from, to continue a long page (default 0)."));

    public ToolEffect EffectOf(JsonElement arguments) => ToolEffect.Searching;

    public async Task<string> RunAsync(JsonElement arguments, CancellationToken cancellation)
    {
        var address = arguments.Text("url").Trim();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ToolException("The url must be a full address starting with http:// or https://.");

        var start = 0;
        if (arguments.OptionalText("start") is { } startText && !int.TryParse(startText, out start))
            throw new ToolException("'start' must be a whole number.");
        start = Math.Max(start, 0);

        var (body, contentType) = await DownloadAsync(uri, cancellation);
        var isHtml = contentType.Contains("html", StringComparison.OrdinalIgnoreCase) || body.TrimStart().StartsWith('<');
        var text = isHtml ? HtmlText.From(body) : body;

        if (text.Length == 0) return $"{address} has no readable text. {TryAnother}";
        if (isHtml && start == 0 && body.Length >= ScriptedPageBytes && text.Length < ScriptedPageChars)
            return $"{text}\n[{address} fills in its content with scripts, so this is all the text it has. {TryAnother}]";
        if (start >= text.Length) return $"{address} has only {text.Length} characters.";

        var end = Math.Min(start + Limits.WebPageChars, text.Length);
        var page = text[start..end];
        return end < text.Length
            ? $"{page}\n[characters {start}-{end} of {text.Length}; call again with start={end} to read on]"
            : page;
    }

    private async Task<(string Body, string ContentType)> DownloadAsync(Uri uri, CancellationToken cancellation)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Homunculus");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode)
                throw new ToolException($"{uri} answered {(int)response.StatusCode} {response.ReasonPhrase}. {TryAnother}");

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Length > 0 && !IsText(contentType))
                throw new ToolException($"{uri} is {contentType}, not a page of text. {TryAnother}");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while (buffer.Length < MaxDownloadBytes && (read = await stream.ReadAsync(chunk, cancellation)) > 0)
                buffer.Write(chunk, 0, read);

            var encoding = System.Text.Encoding.UTF8;
            if (response.Content.Headers.ContentType?.CharSet is { } charset)
            {
                try { encoding = System.Text.Encoding.GetEncoding(charset); }
                catch (ArgumentException) { }
            }

            return (encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), contentType);
        }
        catch (HttpRequestException e)
        {
            throw new ToolException($"{uri} could not be reached: {e.Message} {TryAnother}");
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new ToolException($"{uri} took too long to answer. {TryAnother}");
        }
    }

    private static bool IsText(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase);
}
