using System.Net;
using System.Text.Json.Nodes;
using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Llm;

public class OpenAiModelTests
{
    private const string TextReply = """{"choices":[{"message":{"role":"assistant","content":"hi"}}]}""";

    private static readonly ToolSpec Spec = new("read_file", "Reads", new JsonObject { ["type"] = "object" });

    private static (OpenAiModel Model, StubHttpHandler Handler) Create(string response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHttpHandler(status, response);
        return (new OpenAiModel(OpenAiModel.CreateClient("http://host/v1/", "secret", handler), "my-model"), handler);
    }

    private static async Task<Message> Complete(OpenAiModel model, Message[] conversation, params ToolSpec[] tools) =>
        (await model.CompleteAsync(new FixedConversation(conversation), tools, CancellationToken.None)).Message;

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
    public async Task SendsToolDescriptions()
    {
        var (model, handler) = Create(TextReply);

        await Complete(model, [Message.User("hello")], Spec);

        var tool = SentJson(handler)["tools"]![0]!;
        Assert.That(tool["type"]!.GetValue<string>(), Is.EqualTo("function"));
        Assert.That(tool["function"]!["name"]!.GetValue<string>(), Is.EqualTo("read_file"));
        Assert.That(tool["function"]!["parameters"]!["type"]!.GetValue<string>(), Is.EqualTo("object"));
    }

    [Test]
    public async Task OmitsTheToolsFieldWhenThereAreNone()
    {
        var (model, handler) = Create(TextReply);

        await Complete(model, [Message.User("hello")]);

        Assert.That(SentJson(handler)["tools"], Is.Null);
    }

    [Test]
    public async Task CanBeCalledTwiceWithTheSameToolSpecs()
    {
        var (model, _) = Create(TextReply);

        await Complete(model, [Message.User("one")], Spec);

        Assert.DoesNotThrowAsync(() => Complete(model, [Message.User("two")], Spec));
    }

    [Test]
    public async Task SendsAssistantToolCallsAndToolResults()
    {
        var (model, handler) = Create(TextReply);
        var call = new ToolCall("c1", "read_file", Json.Parse("""{"path":"a.txt"}"""));

        await Complete(model, [Replies.Calls(call), Message.ToolResult("c1", "file text")]);

        var messages = SentJson(handler)["messages"]!;
        var sentCall = messages[0]!["tool_calls"]![0]!;
        Assert.That(sentCall["id"]!.GetValue<string>(), Is.EqualTo("c1"));
        Assert.That(sentCall["function"]!["name"]!.GetValue<string>(), Is.EqualTo("read_file"));
        Assert.That(sentCall["function"]!["arguments"]!.GetValue<string>(), Is.EqualTo("""{"path":"a.txt"}"""));
        Assert.That(messages[1]!["role"]!.GetValue<string>(), Is.EqualTo("tool"));
        Assert.That(messages[1]!["tool_call_id"]!.GetValue<string>(), Is.EqualTo("c1"));
    }

    [Test]
    public async Task ParsesATextReply()
    {
        var (model, _) = Create(TextReply);

        var reply = await Complete(model, [Message.User("hello")]);

        Assert.That(reply.Role, Is.EqualTo(Role.Assistant));
        Assert.That(reply.Content, Is.EqualTo("hi"));
        Assert.That(reply.HasToolCalls, Is.False);
    }

    [Test]
    public async Task ParsesToolCalls()
    {
        var (model, _) = Create("""
            {"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[
              {"id":"c9","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.txt\"}"}}]}}]}
            """);

        var reply = await Complete(model, [Message.User("hello")]);

        var call = reply.ToolCalls!.Single();
        Assert.That(reply.Content, Is.Null);
        Assert.That(call.Id, Is.EqualTo("c9"));
        Assert.That(call.Name, Is.EqualTo("read_file"));
        Assert.That(call.Arguments.GetProperty("path").GetString(), Is.EqualTo("a.txt"));
    }

    [TestCase("\"\"")]
    [TestCase("null")]
    public async Task TreatsEmptyArgumentsAsAnEmptyObject(string arguments)
    {
        var (model, _) = Create($$$"""
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"id":"c1","type":"function","function":{"name":"list_files","arguments":{{{arguments}}}}}]}}]}
            """);

        var reply = await Complete(model, [Message.User("hello")]);

        Assert.That(reply.ToolCalls!.Single().Arguments.EnumerateObject(), Is.Empty);
    }

    [Test]
    public async Task ParsesSeveralToolCallsInOrder()
    {
        var (model, _) = Create("""
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"id":"a","type":"function","function":{"name":"one","arguments":"{}"}},
              {"id":"b","type":"function","function":{"name":"two","arguments":"{}"}}]}}]}
            """);

        var reply = await Complete(model, [Message.User("hello")]);

        Assert.That(reply.ToolCalls!.Select(c => c.Name), Is.EqualTo(new[] { "one", "two" }));
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

    [TestCase("""{"choices":[{"finish_reason":"length","message":{"content":"par"}}]}""")]
    [TestCase("""{"choices":[]}""")]
    [TestCase("""{}""")]
    [TestCase("not json")]
    [TestCase("""{"choices":[{"message":{"content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"x","arguments":"{broken"}}]}}]}""")]
    [TestCase("""{"choices":[{"message":{"content":null,"tool_calls":[{"type":"function","function":{"name":"x","arguments":"{}"}}]}}]}""")]
    public void ReportsAMalformedResponseAsAModelError(string response)
    {
        var (model, _) = Create(response);

        Assert.ThrowsAsync<ModelException>(() => Complete(model, [Message.User("hello")]));
    }
}
