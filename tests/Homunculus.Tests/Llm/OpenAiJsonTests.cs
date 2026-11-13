using System.Text.Json.Nodes;
using Homunculus.Llm;

namespace Homunculus.Tests.Llm;

/// <summary>The conversion between our messages and the chat-completions JSON, without any HTTP.</summary>
public class OpenAiJsonTests
{
    private static JsonNode Request(IConversation conversation) =>
        JsonNode.Parse(OpenAiJson.BuildRequest("my-model", conversation))!;

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
    public void ReadsATextReply()
    {
        var message = OpenAiJson.ParseReply("""{"choices":[{"message":{"role":"assistant","content":"hi"}}]}""").Message;

        Assert.That((message.Role, message.Content), Is.EqualTo((Role.Assistant, (string?)"hi")));
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

    [Test]
    public void ReadsTheReasoningOfAReasoningModelEvenWhenTheContentIsEmpty()
    {
        var reply = OpenAiJson.ParseReply("""
            {"id":"chatcmpl-417","object":"chat.completion","model":"qwen3:8b",
             "choices":[{"index":0,"message":{"role":"assistant","content":"","reasoning":"I will list the files first.\n"},"finish_reason":"stop"}],
             "usage":{"prompt_tokens":601,"prompt_tokens_details":{"cached_tokens":571},"completion_tokens":177,"total_tokens":778}}
            """);

        Assert.That(reply.Reasoning, Is.EqualTo("I will list the files first."));
        Assert.That(reply.Message.Content, Is.Empty);
        Assert.That(reply.Usage, Is.EqualTo(new TokenUsage(601, 177, 778, Cached: 571)));
    }

    [Test]
    public void AlsoReadsTheReasoningFromReasoningContent() =>
        Assert.That(
            OpenAiJson.ParseReply("""{"choices":[{"message":{"content":"hi","reasoning_content":"because"}}]}""").Reasoning,
            Is.EqualTo("because"));

    [TestCase("""{"choices":[{"message":{"content":"hi"}}]}""")]
    [TestCase("""{"choices":[{"message":{"content":"hi","reasoning":"  "}}]}""")]
    [TestCase("""{"choices":[{"message":{"content":"hi","reasoning":null}}]}""")]
    public void HasNoReasoningWhenTheServerSendsNone(string body) =>
        Assert.That(OpenAiJson.ParseReply(body).Reasoning, Is.Null);

    [Test]
    public void MarksAReplyThatWasCutOffByTheOutputLimit()
    {
        var reply = OpenAiJson.ParseReply(
            """{"choices":[{"finish_reason":"length","message":{"content":"par"}}]}""");

        Assert.That(reply.CutOff, Is.True);
        Assert.That(reply.Message.Content, Is.EqualTo("par"));
    }

    [Test]
    public void AcceptsAnyOtherFinishReason() =>
        Assert.That(
            OpenAiJson.ParseReply("""{"choices":[{"finish_reason":"stop","message":{"content":"hi"}}]}""").Message.Content,
            Is.EqualTo("hi"));

    [TestCase("""{"choices":[]}""", "no choices[0].message")]
    [TestCase("""{}""", "no choices[0].message")]
    [TestCase("""{"choices":[{}]}""", "no choices[0].message")]
    public void RejectsAReplyThatIsMissingWhatItNeeds(string body, string expectedMessage)
    {
        var error = Assert.Throws<FormatException>(() => OpenAiJson.ParseReply(body));

        Assert.That(error!.Message, Is.EqualTo(expectedMessage));
    }

    [TestCase("not json")]
    public void RejectsABodyThatIsNotValidJson(string body) =>
        Assert.Throws<FormatException>(() => OpenAiJson.ParseReply(body));
}
