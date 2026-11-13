using System.Net;
using System.Text.Json;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Tests.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Logging;

public class JsonlEventLogTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), $"homunculus-log-{Guid.NewGuid():N}", "nested", "log.jsonl");

    [TearDown]
    public void TearDown()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(_path))!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private JsonElement[] Lines()
    {
        // The log may still be open for writing, so read with sharing.
        using var reader = new StreamReader(new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l).RootElement).ToArray();
    }

    [Test]
    public void WritesOneJsonObjectPerLineAndCreatesTheDirectory()
    {
        using (var log = new JsonlEventLog(_path, () => new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero)))
        {
            log.Write("first", new { n = 1 });
            log.Write("second");
        }

        var lines = Lines();
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0].GetProperty("event").GetString(), Is.EqualTo("first"));
        Assert.That(lines[0].GetProperty("data").GetProperty("n").GetInt32(), Is.EqualTo(1));
        Assert.That(lines[0].GetProperty("time").GetString(), Does.StartWith("2026-10-22T09:00:00"));
        Assert.That(lines[1].GetProperty("event").GetString(), Is.EqualTo("second"));
    }

    [Test]
    public void KeepsMultiLineTextOnASingleLine()
    {
        using (var log = new JsonlEventLog(_path)) log.Write("e", new { text = "a\nb" });

        Assert.That(File.ReadAllLines(_path), Has.Length.EqualTo(1));
    }

    [Test]
    public void RawKeepsJsonAsJsonAndOtherTextAsText()
    {
        Assert.That(JsonlEventLog.Raw("""{"a":1}"""), Is.InstanceOf<JsonElement>());
        Assert.That(JsonlEventLog.Raw("<html>bad gateway</html>"), Is.EqualTo("<html>bad gateway</html>"));
    }

    [Test]
    public void TruncatesLongText()
    {
        Assert.That(JsonlEventLog.Truncate("abc", 10), Is.EqualTo("abc"));
        Assert.That(JsonlEventLog.Truncate(new string('x', 20), 10), Does.StartWith("xxxxxxxxxx").And.Contain("10 more chars"));
    }

    [Test]
    public async Task ModelLogsTheRawResponseBodyAndNeverTheApiKey()
    {
        const string reply = """{"choices":[{"message":{"role":"assistant","content":"hi","reasoning":"thinking it over"}}]}""";
        using (var log = new JsonlEventLog(_path))
        {
            var client = OpenAiModel.CreateClient("http://host/v1/", "secret-key", new StubHttpHandler(HttpStatusCode.OK, reply));
            await new OpenAiModel(client, "my-model", log).CompleteAsync(new FixedConversation([Message.User("hello")]), [], CancellationToken.None);
        }

        var lines = Lines();
        Assert.That(lines.Select(l => l.GetProperty("event").GetString()), Is.EqualTo(new[] { "llm_request", "llm_response" }));
        var data = lines[1].GetProperty("data");
        Assert.That(data.GetProperty("status").GetInt32(), Is.EqualTo(200));
        Assert.That(data.GetProperty("body").GetProperty("choices")[0].GetProperty("message").GetProperty("reasoning").GetString(), Is.EqualTo("thinking it over"));
        Assert.That(File.ReadAllText(_path), Does.Not.Contain("secret-key"));
    }

    [Test]
    public void ModelLogsAFailedCallsBodyBeforeThrowing()
    {
        using var log = new JsonlEventLog(_path);
        var client = OpenAiModel.CreateClient("http://host/v1/", "k", new StubHttpHandler(HttpStatusCode.BadGateway, "upstream down"));
        var model = new OpenAiModel(client, "m", log);

        Assert.ThrowsAsync<ModelException>(() => model.CompleteAsync(new FixedConversation([Message.User("x")]), [], CancellationToken.None));

        Assert.That(Lines().Last().GetProperty("data").GetProperty("body").GetString(), Is.EqualTo("upstream down"));
    }
}
