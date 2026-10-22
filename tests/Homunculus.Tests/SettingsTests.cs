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
        Assert.That(settings.Model, Is.EqualTo("gpt-5-mini"));
        Assert.That(settings.JudgeModel, Is.EqualTo("gpt-5-mini"));
        Assert.That(settings.CommandTimeout, Is.EqualTo(TimeSpan.FromSeconds(600)));
        Assert.That(settings.Iterations.Normal, Is.EqualTo(8));
        Assert.That(settings.Iterations.WhenPlanning, Is.EqualTo(24));
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
    public void TheJudgeModelFollowsTheModelUnlessSet()
    {
        var same = From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_MODEL", "big"));
        var different = From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_MODEL", "big"), ("HOMUNCULUS_JUDGE_MODEL", "small"));

        Assert.That(same.JudgeModel, Is.EqualTo("big"));
        Assert.That(different.JudgeModel, Is.EqualTo("small"));
    }

    [Test]
    public void ReadsNumericOverrides()
    {
        var settings = From(
            ("OPENAI_API_KEY", "k"),
            ("HOMUNCULUS_MAX_ITERATIONS", "3"),
            ("HOMUNCULUS_MAX_ITERATIONS_WHEN_PLANNING", "5"),
            ("HOMUNCULUS_COMMAND_TIMEOUT_SECONDS", "7"));

        Assert.That(settings.Iterations.Normal, Is.EqualTo(3));
        Assert.That(settings.Iterations.WhenPlanning, Is.EqualTo(5));
        Assert.That(settings.CommandTimeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void FallsBackToTheDefaultForANonNumericValue() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_MAX_ITERATIONS", "many")).Iterations.Normal, Is.EqualTo(8));

    [Test]
    public void TreatsEmptyValuesAsUnset() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_MODEL", "")).Model, Is.EqualTo("gpt-5-mini"));

    [Test]
    public void AllToolsAreEnabledUnlessListed() =>
        Assert.That(From(("OPENAI_API_KEY", "k")).EnabledTools, Is.Null);

    [Test]
    public void ReadsTheEnabledToolsAsATrimmedList() =>
        Assert.That(
            From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_TOOLS", "read_file, list_files ,")).EnabledTools,
            Is.EqualTo(new[] { "read_file", "list_files" }));

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
