namespace Homunculus.Tests;

public class CheckCommandSettingsTests
{
    private static Settings From(params (string Name, string Value)[] variables)
    {
        var map = variables.ToDictionary(v => v.Name, v => v.Value);
        return Settings.From(name => map.GetValueOrDefault(name));
    }

    [Test]
    public void HasNoCheckUnlessOneIsSet() =>
        Assert.That(From(("OPENAI_API_KEY", "k")).CheckCommand, Is.Null);

    [Test]
    public void ReadsTheCheckCommandAsItIsWritten() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_CHECK_COMMAND", "dotnet build --no-restore -v q")).CheckCommand,
            Is.EqualTo("dotnet build --no-restore -v q"));

    [Test]
    public void IgnoresAnEmptyCheckCommand() =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_CHECK_COMMAND", "")).CheckCommand, Is.Null);

    [Test]
    public void UndoesBreakingEditsUnlessTurnedOff() =>
        Assert.That(From(("OPENAI_API_KEY", "k")).CheckRevert, Is.True);

    [TestCase("false", false)]
    [TestCase("False", false)]
    [TestCase("true", true)]
    [TestCase("maybe", true)]
    [TestCase("", true)]
    public void ReadsTheRevertSwitch(string value, bool expected) =>
        Assert.That(From(("OPENAI_API_KEY", "k"), ("HOMUNCULUS_CHECK_REVERT", value)).CheckRevert, Is.EqualTo(expected));
}
