using Homunculus.Tests.Support;
using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class CheckSelectionTests
{
    private TempProject _project = null!;

    [SetUp] public void SetUp() => _project = new TempProject();

    [TearDown]
    public void TearDown()
    {
        _project?.Dispose();
        _project = null!;
    }

    private static CheckDetector Everything => new(_ => true, false, "TEMP");

    private static CheckDetector Nothing => new(_ => false, false, "TEMP");

    private static CheckDetector Unused => new(_ => throw new InvalidOperationException("must not look"), false, "TEMP");

    [Test]
    public void DetectsTheCommandWhenNothingIsSet()
    {
        _project.Write("go.mod", "module x");

        var choice = CheckSelection.Choose(null, _project.Root, Everything);

        Assert.That(choice.Command, Is.EqualTo("go build ./..."));
        Assert.That(choice.Note, Is.EqualTo("check: go build ./... (found for Go; set HOMUNCULUS_CHECK_COMMAND to change it, or to off)"));
    }

    [Test]
    public void HasNoCheckAndNothingToSayWhenNothingIsRecognised()
    {
        var choice = CheckSelection.Choose(null, _project.Root, Everything);

        Assert.That(choice, Is.EqualTo(new CheckSelection(null, null)));
    }

    [Test]
    public void HasNoCheckWhenTheToolIsNotInstalled()
    {
        _project.Write("go.mod", "module x");

        Assert.That(CheckSelection.Choose(null, _project.Root, Nothing), Is.EqualTo(new CheckSelection(null, null)));
    }

    [TestCase("off")]
    [TestCase("OFF")]
    [TestCase(" none ")]
    [TestCase("false")]
    [TestCase("no")]
    [TestCase("0")]
    [TestCase("disabled")]
    public void TurnsTheCheckOffWithoutLookingAtTheProject(string setting)
    {
        _project.Write("go.mod", "module x");

        Assert.That(CheckSelection.Choose(setting, _project.Root, Unused), Is.EqualTo(new CheckSelection(null, null)));
    }

    [Test]
    public void UsesAnExplicitCommandAsItIsWithoutLookingAtTheProject()
    {
        var choice = CheckSelection.Choose("make lint", _project.Root, Unused);

        Assert.That(choice, Is.EqualTo(new CheckSelection("make lint", "check: make lint")));
    }

    [Test]
    public void DetectsWhenTheSettingWasEmptyAndSoBecameNull()
    {
        // Settings.From turns an empty variable into null, so detection still happens.
        _project.Write("Cargo.toml", "[package]");

        Assert.That(CheckSelection.Choose(null, _project.Root, Everything).Command, Does.StartWith("cargo check"));
    }
}
