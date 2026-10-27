using Homunculus.Tools;

namespace Homunculus.Tests.Tools;

public class ClosestLineTests
{
    private const string Source = "using System;\nvar commands = new CommandRegistry();\ncommands\n    .Add(new HelpCommand(commands, ui))\n    .Add(new PolicyCommand(Policy, ui))\n    .Add(new ExitCommand());\n";

    [Test]
    public void FindsTheLineThatSharesTheRarestIdentifier()
    {
        var found = ClosestLine.Find(Source, "registry.Register<PolicyCommand>();");

        Assert.That(found, Is.EqualTo((5, ".Add(new PolicyCommand(Policy, ui))")));
    }

    [Test]
    public void LooksOnlyAtTheFirstLineThatHasWords()
    {
        var found = ClosestLine.Find(Source, "\n  {\n.Add(new ExitCommand());\nUnrelated();");

        Assert.That(found!.Value.Number, Is.EqualTo(6));
    }

    [Test]
    public void IgnoresLineEndingsAndIndentationInWhatItReturns()
    {
        var found = ClosestLine.Find("one\r\n    two words\r\nthree\r\n", "two");

        Assert.That(found, Is.EqualTo((2, "two words")));
    }

    [Test]
    public void FindsNothingWhenNoWordIsShared() =>
        Assert.That(ClosestLine.Find(Source, "zzz_unrelated()"), Is.Null);

    [Test]
    public void FindsNothingForTextWithoutWords() =>
        Assert.That(ClosestLine.Find(Source, "  {};\n"), Is.Null);

    [Test]
    public void CutsAVeryLongLine()
    {
        var found = ClosestLine.Find(new string('x', 500), "xxxxxxxxxx");

        Assert.That(found, Is.Null, "a different word than the long one");

        var same = ClosestLine.Find(new string('x', 500), new string('x', 500));

        Assert.That(same!.Value.Text, Has.Length.EqualTo(203).And.EndsWith("..."));
    }
}
