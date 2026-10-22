using System.Text.Json.Nodes;
using Homunculus.Llm;
using Homunculus.Tests.Support;

namespace Homunculus.Tests.Llm;

/// <summary>The conversion between our messages and the chat-completions JSON, without any HTTP.</summary>
public class OpenAiJsonTests
{
    private static readonly ToolSpec Spec = new("read_file", "Reads", new JsonObject { ["type"] = "object" });

    private static JsonNode Request(IConversation conversation, params ToolSpec[] tools) =>
        JsonNode.Parse(OpenAiJson.BuildRequest("my-model", conversation, tools))!;

    private static Conversation Chat(params Message[] messages)
    {
        var conversation = new Conversation("be brief");
        foreach (var message in messages) conversation.Add(message);
        return conversation;
    }

    [Test]
    public void WritesTheModelAndTheMessagesWithLowercaseRoles()
    {
        var json = Request(Chat(Message.User("hello"), Message.Assistant("hi")));

        Assert.That(json["model"]!.GetValue<string>(), Is.EqualTo("my-model"));
        Assert.That(json["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()),
            Is.EqualTo(new[] { "system", "user", "assistant" }));
    }

    [Test]
    public void LeavesOutTheToolsWhenThereAreNone() =>
        Assert.That(Request(Chat())["tools"], Is.Null);

    [Test]
    public void DescribesEachToolAsAFunction()
    {
        var tool = Request(Chat(), Spec)["tools"]![0]!;

        Assert.That(tool["type"]!.GetValue<string>(), Is.EqualTo("function"));
        Assert.That(tool["function"]!["name"]!.GetValue<string>(), Is.EqualTo("read_file"));
        Assert.That(tool["function"]!["parameters"]!["type"]!.GetValue<string>(), Is.EqualTo("object"));
    }

    [Test]
    public void SendsToolCallArgumentsAsAJsonString()
    {
        var call = new ToolCall("c1", "read_file", Json.Parse("""{"path":"a.txt"}"""));

        var sent = Request(Chat(Message.Assistant(null, [call])))["messages"]![1]!;

        var function = sent["tool_calls"]![0]!["function"]!;
        Assert.That(sent["tool_calls"]![0]!["id"]!.GetValue<string>(), Is.EqualTo("c1"));
        Assert.That(function["name"]!.GetValue<string>(), Is.EqualTo("read_file"));
        Assert.That(function["arguments"]!.GetValue<string>(), Is.EqualTo("""{"path":"a.txt"}"""));
    }

    [Test]
    public void LinksAToolResultToItsCallAndOmitsEmptyFields()
    {
        var sent = Request(Chat(Message.ToolResult("c1", "output")))["messages"]![1]!.AsObject();

        Assert.That(sent["role"]!.GetValue<string>(), Is.EqualTo("tool"));
        Assert.That(sent["tool_call_id"]!.GetValue<string>(), Is.EqualTo("c1"));
        Assert.That(sent.ContainsKey("tool_calls"), Is.False);
    }

    [Test]
    public void ReadsATextReply()
    {
        var message = OpenAiJson.ParseReply("""{"choices":[{"message":{"role":"assistant","content":"hi"}}]}""").Message;

        Assert.That((message.Role, message.Content, message.HasToolCalls), Is.EqualTo((Role.Assistant, (string?)"hi", false)));
    }

    [Test]
    public void ReadsToolCallsAndParsesTheirArguments()
    {
        var message = OpenAiJson.ParseReply("""
            {"choices":[{"message":{"content":null,"tool_calls":[
              {"id":"c1","type":"function","function":{"name":"read_file","arguments":"{\"path\":\"a.txt\"}"}}]}}]}
            """).Message;

        var call = message.ToolCalls!.Single();
        Assert.That((call.Id, call.Name), Is.EqualTo(("c1", "read_file")));
        Assert.That(call.Arguments.GetProperty("path").GetString(), Is.EqualTo("a.txt"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void TreatsBlankArgumentsAsAnEmptyObject(string arguments)
    {
        var message = OpenAiJson.ParseReply($$$"""
            {"choices":[{"message":{"tool_calls":[{"id":"c1","function":{"name":"x","arguments":"{{{arguments}}}"}}]}}]}
            """).Message;

        Assert.That(message.ToolCalls!.Single().Arguments.EnumerateObject(), Is.Empty);
    }

    [Test]
    public void UsesOnlyTheFirstChoice()
    {
        var message = OpenAiJson.ParseReply("""{"choices":[{"message":{"content":"one"}},{"message":{"content":"two"}}]}""").Message;

        Assert.That(message.Content, Is.EqualTo("one"));
    }

    [Test]
    public void ReadsTheTokenUsage()
    {
        var reply = OpenAiJson.ParseReply("""
            {"choices":[{"message":{"content":"hi"}}],
             "usage":{"prompt_tokens":709,"prompt_tokens_details":{"cached_tokens":603},"completion_tokens":207,"total_tokens":916}}
            """);

        Assert.That(reply.Usage, Is.EqualTo(new TokenUsage(Input: 709, Output: 207, Total: 916, Cached: 603)));
    }

    [Test]
    public void ReportsNoUsageWhenTheServerSendsNone() =>
        Assert.That(OpenAiJson.ParseReply("""{"choices":[{"message":{"content":"hi"}}]}""").Usage, Is.Null);

    [Test]
    public void CountsMissingUsageFieldsAsZeroAndDerivesTheTotal()
    {
        var reply = OpenAiJson.ParseReply("""{"choices":[{"message":{"content":"hi"}}],"usage":{"prompt_tokens":10,"completion_tokens":5}}""");

        Assert.That(reply.Usage, Is.EqualTo(new TokenUsage(Input: 10, Output: 5, Total: 15, Cached: 0)));
    }

    [Test]
    public void ToleratesNullUsageValues()
    {
        var reply = OpenAiJson.ParseReply("""
            {"choices":[{"message":{"content":"hi"}}],
             "usage":{"prompt_tokens":null,"prompt_tokens_details":null,"completion_tokens":7,"total_tokens":null}}
            """);

        Assert.That(reply.Usage, Is.EqualTo(new TokenUsage(Input: 0, Output: 7, Total: 7, Cached: 0)));
    }

    [TestCase("""{"choices":[]}""", "no choices[0].message")]
    [TestCase("""{}""", "no choices[0].message")]
    [TestCase("""{"choices":[{}]}""", "no choices[0].message")]
    [TestCase("""{"choices":[{"message":{"tool_calls":[{"function":{"name":"x","arguments":"{}"}}]}}]}""", "tool call without id")]
    [TestCase("""{"choices":[{"message":{"tool_calls":[{"id":"c1"}]}}]}""", "tool call without function")]
    [TestCase("""{"choices":[{"message":{"tool_calls":[{"id":"c1","function":{"arguments":"{}"}}]}}]}""", "tool call without name")]
    public void RejectsAReplyThatIsMissingWhatItNeeds(string body, string expectedMessage)
    {
        var error = Assert.Throws<FormatException>(() => OpenAiJson.ParseReply(body));

        Assert.That(error!.Message, Is.EqualTo(expectedMessage));
    }

    [TestCase("not json")]
    [TestCase("""{"choices":[{"message":{"tool_calls":[{"id":"c1","function":{"name":"x","arguments":"{broken"}}]}}]}""")]
    public void RejectsABodyThatIsNotValidJson(string body) =>
        Assert.Throws<FormatException>(() => OpenAiJson.ParseReply(body));
}
