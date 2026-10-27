using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ClosestLineTests
{
    private const string Source = "using System;\nvar commands = new CommandRegistry();\ncommands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new PolicyCommand(Policy, ui))\n    .Add(new ExitCommand());\n";

    [Test]
    public void FindsTheRealLineSharingTheRarestIdentifier()
    {
        var found = ClosestLine.Find(Source, "registry.Register<PolicyCommand>();")!;

        Assert.That((found.QuotedNumber, found.QuotedText), Is.EqualTo((1, "registry.Register<PolicyCommand>();")));
        Assert.That((found.ClosestNumber, found.ClosestText), Is.EqualTo((5, "    .Add(new PolicyCommand(Policy, ui))")));
    }

    [Test]
    public void PointsAtTheFirstQuotedLineThatIsNotInTheFileNotAtTheFirstLine()
    {
        var quoted = "commands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new TooolsCommand(Tools, ui))\n    .Add(new ExitCommand());";

        var found = ClosestLine.Find(Source, quoted)!;

        Assert.That(found.QuotedNumber, Is.EqualTo(3));
        Assert.That(found.QuotedText, Is.EqualTo("    .Add(new TooolsCommand(Tools, ui))"));
    }

    [Test]
    public void NoticesALineWhoseIndentationIsWrong()
    {
        var found = ClosestLine.Find(Source, "  .Add(new ExitCommand());")!;

        Assert.That(found.ClosestText, Is.EqualTo("    .Add(new ExitCommand());"));
    }

    [Test]
    public void IgnoresLineEndings()
    {
        var found = ClosestLine.Find("one\r\n    two words\r\nthree\r\n", "two")!;

        Assert.That((found.ClosestNumber, found.ClosestText), Is.EqualTo((2, "    two words")));
    }

    [Test]
    public void SaysWhichLineIsMissingEvenWhenNoRealLineIsSimilar()
    {
        var found = ClosestLine.Find(Source, "zzz_unrelated()")!;

        Assert.That(found.QuotedNumber, Is.EqualTo(1));
        Assert.That(found.ClosestNumber, Is.Null);
        Assert.That(found.ClosestText, Is.Null);
    }

    [Test]
    public void FindsNothingToSayWhenEveryQuotedLineIsInTheFile() =>
        Assert.That(ClosestLine.Find(Source, "    .Add(new ExitCommand());\ncommands"), Is.Null);

    [Test]
    public void SkipsQuotedLinesThatHaveNoWords() =>
        Assert.That(ClosestLine.Find(Source, "  {};\n"), Is.Null);

    [Test]
    public void CutsAVeryLongLine()
    {
        var found = ClosestLine.Find("short\n" + new string('x', 500), new string('x', 400) + "y")!;

        Assert.That(found.QuotedText, Has.Length.EqualTo(203).And.EndsWith("..."));
        Assert.That(found.ClosestNumber, Is.Null, "the long word is not a word of the file");
    }
}
