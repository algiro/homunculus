using System.Net;
using System.Text.Json.Nodes;
using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Llm;

public class OpenAiModelTests
{
    private const string TextReply = """{"choices":[{"message":{"role":"assistant","content":"hi"}}]}""";

    private static (OpenAiModel Model, StubHttpHandler Handler) Create(string response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHttpHandler(status, response);
        return (new OpenAiModel(OpenAiModel.CreateClient("http://host/v1/", "secret", handler), "my-model"), handler);
    }

    private static async Task<Message> Complete(OpenAiModel model, Message[] conversation) =>
        (await model.CompleteAsync(new FixedConversation(conversation), CancellationToken.None)).Message;

    private static JsonNode SentJson(StubHttpHandler handler) => JsonNode.Parse(handler.RequestBody)!;

    [Test]
    public async Task PostsToTheChatCompletionsEndpointWithTheApiKey()
    {
        var (model, handler) = Create(TextReply);

        await Complete(model, [Message.User("hello")]);

        Assert.That(handler.Request!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.Request.RequestUri!.ToString(), Is.EqualTo("http://host/v1/chat/completions"));
        Assert.That(handler.Request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer secret"));
    }

    [Test]
    public async Task SendsTheModelNameAndMessages()
    {
        var (model, handler) = Create(TextReply);

        await Complete(model, [Message.System("be brief"), Message.User("hello")]);

        var sent = SentJson(handler);
        Assert.That(sent["model"]!.GetValue<string>(), Is.EqualTo("my-model"));
        Assert.That(sent["messages"]![0]!["role"]!.GetValue<string>(), Is.EqualTo("system"));
        Assert.That(sent["messages"]![1]!["content"]!.GetValue<string>(), Is.EqualTo("hello"));
    }

    [Test]
    public async Task ParsesATextReply()
    {
        var (model, _) = Create(TextReply);

        var reply = await Complete(model, [Message.User("hello")]);

        Assert.That(reply.Role, Is.EqualTo(Role.Assistant));
        Assert.That(reply.Content, Is.EqualTo("hi"));
    }

    [Test]
    public void ReportsTheStatusAndBodyWhenTheCallFails()
    {
        var (model, _) = Create("""{"error":{"message":"does not support tools"}}""", HttpStatusCode.BadRequest);

        var error = Assert.ThrowsAsync<ModelException>(() => Complete(model, [Message.User("hello")]));

        Assert.That(error!.Message, Does.Contain("400").And.Contain("does not support tools"));
    }

    [Test]
    public void ReportsAnUnreachableServerAsAModelError()
    {
        var model = new OpenAiModel(OpenAiModel.CreateClient("http://host/v1/", "secret", new UnreachableHttpHandler()), "my-model");

        var error = Assert.ThrowsAsync<ModelException>(() => Complete(model, [Message.User("hello")]));

        Assert.That(error!.InnerException, Is.InstanceOf<HttpRequestException>());
    }

    [TestCase("""{"choices":[]}""")]
    [TestCase("""{}""")]
    [TestCase("not json")]
    public void ReportsAMalformedResponseAsAModelError(string response)
    {
        var (model, _) = Create(response);

        Assert.ThrowsAsync<ModelException>(() => Complete(model, [Message.User("hello")]));
    }
}
