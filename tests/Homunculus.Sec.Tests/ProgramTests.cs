namespace Homunculus.Sec.Tests;

public class ProgramTests
{
    [Test]
    public async Task Main_ReturnsZero() => Assert.That(await Program.Main(), Is.EqualTo(0));
}
