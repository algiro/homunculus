namespace Homunculus.Sec.Tests;

public class ProgramTests
{
    [Test]
    public async Task Main_RefusesToStartWithAMissingSettingsFile() =>
        Assert.That(await Program.Main([Path.Combine(Path.GetTempPath(), "homunculus-sec-missing", "config.json")]), Is.EqualTo(2));
}
