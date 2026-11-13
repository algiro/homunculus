namespace Homunculus.Tests;

public class SettingsTests
{
    private static Settings From(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value);
        return Settings.From(name => map.GetValueOrDefault(name));
    }

    [Test]
    public void UsesDefaultsWhenOnlyTheApiKeyIsSet()
    {
        var settings = From(("OPENAI_API_KEY", "sk-1"));

        Assert.That(settings.ApiKey, Is.EqualTo("sk-1"));
        Assert.That(settings.BaseUrl, Is.EqualTo("https://api.openai.com/v1"));
        Assert.That(settings.Model, Is.EqualTo("qwen3:8b"));
    }

    [Test]
    public void RequiresAnApiKeyForTheDefaultEndpoint() =>
        Assert.Throws<InvalidOperationException>(() => From());

    [Test]
    public void DoesNotRequireAnApiKeyForACustomEndpoint()
    {
        var settings = From(("HOMUNCULUS_BASE_URL", "http://localhost:11434/v1"));

        Assert.That(settings.ApiKey, Is.EqualTo("none"));
        Assert.That(settings.BaseUrl, Is.EqualTo("http://localhost:11434/v1"));
    }

    [Test]
    public void TreatsEmptyValuesAsUnset() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_MODEL", "")).Model, Is.EqualTo("qwen3:8b"));

    [Test]
    public void ShowsTheReasoningUnlessSwitchedOff() =>
        Assert.That(From(("OPENAI_API_KEY", "k")).ShowReasoning, Is.True);

    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("true", true)]
    [TestCase("maybe", true)]
    [TestCase("", true)]
    public void ReadsTheShowReasoningSwitch(string value, bool expected) =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_SHOW_REASONING", value)).ShowReasoning, Is.EqualTo(expected));
}
