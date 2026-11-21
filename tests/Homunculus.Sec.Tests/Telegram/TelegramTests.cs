using System.Text.Json.Nodes;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Tests.Telegram;

public class TelegramTests
{
    [Test]
    public void ParsesMessagesAndButtonPresses()
    {
        var result = JsonNode.Parse(
            """
            [
              {"update_id":10,"message":{"message_id":5,"from":{"id":42},"chat":{"id":42},"text":"yes","reply_to_message":{"message_id":3}}},
              {"update_id":11,"callback_query":{"id":"abc","from":{"id":42},"message":{"message_id":3,"chat":{"id":42}},"data":"a:7"}},
              {"update_id":12,"edited_message":{"text":"ignored kind"}}
            ]
            """);

        var updates = TelegramApi.ParseUpdates(result);

        Assert.That(updates, Has.Count.EqualTo(3));
        Assert.That(updates[0].Message, Is.EqualTo(new IncomingMessage(42, 42, 5, "yes", 3)));
        Assert.That(updates[1].Press, Is.EqualTo(new ButtonPress("abc", 42, 42, 3, "a:7")));
        Assert.That(updates[2].Message, Is.Null);
        Assert.That(updates[2].Press, Is.Null);
        Assert.That(updates[2].Id, Is.EqualTo(12));
    }

    [Test]
    public void ParsingAnythingUnexpectedGivesNoUpdates()
    {
        Assert.That(TelegramApi.ParseUpdates(null), Is.Empty);
        Assert.That(TelegramApi.ParseUpdates(JsonNode.Parse("{}")), Is.Empty);
    }

    [Test]
    public async Task RequestsGoToTheBotApiEvenThoughATokenLooksLikeAUriScheme()
    {
        // A real token looks like "123456789:AAH..."; as a relative URL the part before the colon reads as a URI scheme.
        var handler = new RecordingHandler("""{"ok":true,"result":[]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };

        await new TelegramApi(http, "123456789:AAH-example_token").GetUpdatesAsync(0, 1, CancellationToken.None);

        Assert.That(handler.Requests.Single()!.ToString(), Is.EqualTo("https://api.telegram.org/bot123456789:AAH-example_token/getUpdates"));
    }

    [Test]
    public async Task TheBodyIsSentWithAContentLengthNotChunked()
    {
        var handler = new RecordingHandler("""{"ok":true,"result":{"message_id":1}}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.telegram.org/") };

        await new TelegramApi(http, "123456789:AAH-example_token").SendMessageAsync(42, "hi", null, CancellationToken.None);

        Assert.That(handler.ContentLengths.Single(), Is.Not.Null, "a chunked body (no Content-Length) makes the Bot API wait forever");
        Assert.That(handler.TransferEncodingChunked.Single(), Is.Not.True);
    }

    [Test]
    public void AnyFailureOfACallIsReportedAsATelegramException()
    {
        using var http = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("https://api.telegram.org/") };

        Assert.ThrowsAsync<TelegramException>(() => new TelegramApi(http, "1:a").GetUpdatesAsync(0, 1, CancellationToken.None));
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public List<Uri?> Requests { get; } = [];
        public List<long?> ContentLengths { get; } = [];
        public List<bool?> TransferEncodingChunked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri);
            ContentLengths.Add(request.Content?.Headers.ContentLength);
            TransferEncodingChunked.Add(request.Headers.TransferEncodingChunked);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The 'bot1' scheme is not supported.");
    }
}
