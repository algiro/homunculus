using System.Text.Json.Nodes;
using Homunculus.Llm;

namespace Homunculus.Tests.Llm;

public class ReasoningEffortTests
{
    private static Settings From(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value);
        return Settings.From(name => map.GetValueOrDefault(name));
    }

    private static JsonNode Request(string? effort) =>
        JsonNode.Parse(OpenAiJson.BuildRequest("m", new Conversation("be brief"), [], effort))!;

    [Test]
    public void IsNotSetUnlessAsked() =>
        Assert.That(From(("OPENAI_API_KEY", "k")).ReasoningEffort, Is.Null);

    [TestCase("none")]
    [TestCase("low")]
    [TestCase("medium")]
    [TestCase("high")]
    public void ReadsTheEffort(string value) =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_REASONING_EFFORT", value)).ReasoningEffort, Is.EqualTo(value));

    [Test]
    public void ReadsTheEffortInAnyCaseAndSendsItLowercase() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_REASONING_EFFORT", "NONE")).ReasoningEffort, Is.EqualTo("none"));

    [Test]
    public void IgnoresAnEmptyEffort() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_REASONING_EFFORT", "")).ReasoningEffort, Is.Null);

    [Test]
    public void RefusesAnUnknownEffortAndNamesTheChoices()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_REASONING_EFFORT", "extreme")));

        Assert.That(error!.Message, Does.Contain("extreme").And.Contain("none, low, medium or high"));
    }

    [Test]
    public void TheRequestCarriesTheEffortWhenSet() =>
        Assert.That(Request("none")["reasoning_effort"]!.GetValue<string>(), Is.EqualTo("none"));

    [Test]
    public void TheRequestLeavesItOutWhenNotSet() =>
        Assert.That(Request(null).AsObject().ContainsKey("reasoning_effort"), Is.False);

    [Test]
    public async Task TheModelSendsTheEffortItWasGiven()
    {
        var handler = new StubHttpHandler(System.Net.HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"hi"}}]}""");
        var model = new OpenAiModel(OpenAiModel.CreateClient("http://host/v1/", "k", handler), "m", reasoningEffort: "low");

        await model.CompleteAsync(new Conversation("be brief"), [], CancellationToken.None);

        Assert.That(JsonNode.Parse(handler.RequestBody)!["reasoning_effort"]!.GetValue<string>(), Is.EqualTo("low"));
    }

    [Test]
    public void TheJudgeDoesNotInheritTheAgentsEffort()
    {
        var settings = From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_REASONING_EFFORT", "none"));

        Assert.That(settings.JudgeReasoningEffort, Is.Null);
    }

    [Test]
    public void ReadsTheJudgesEffortOnItsOwn()
    {
        var settings = From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_JUDGE_REASONING_EFFORT", "Low"));

        Assert.That(settings.JudgeReasoningEffort, Is.EqualTo("low"));
        Assert.That(settings.ReasoningEffort, Is.Null);
    }

    [Test]
    public void RefusesAnUnknownJudgeEffortAndNamesTheVariable()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_JUDGE_REASONING_EFFORT", "extreme")));

        Assert.That(error!.Message, Does.StartWith("HOMUNCULUS_JUDGE_REASONING_EFFORT must be none, low, medium or high"));
    }
}
