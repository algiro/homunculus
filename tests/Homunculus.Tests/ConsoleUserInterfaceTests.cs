namespace Homunculus.Tests;

public class ConsoleUserInterfaceTests
{
    [Test]
    public void PrefixesEveryLineOfReasoning()
    {
        var text = ConsoleUserInterface.PrefixLines("first\n\nsecond\r\nthird");

        Assert.That(text.Split(Environment.NewLine), Is.EqualTo(new[] { "[thinking] first", "[thinking]", "[thinking] second", "[thinking] third" }));
    }

    [Test]
    public void QuitHintNamesTheEndOfInputKeyOfThePlatform()
    {
        Assert.That(ConsoleUserInterface.QuitHint(windows: false), Is.EqualTo("Use /exit or Ctrl-D to quit."));
        Assert.That(ConsoleUserInterface.QuitHint(windows: true), Is.EqualTo("Use /exit or Ctrl-Z then Enter to quit."));
    }

    [Test]
    public void PrefixesSingleLineReasoning() =>
        Assert.That(ConsoleUserInterface.PrefixLines("hmm"), Is.EqualTo("[thinking] hmm"));
}
