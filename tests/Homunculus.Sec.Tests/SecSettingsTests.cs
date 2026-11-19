namespace Homunculus.Sec.Tests;

public class SecSettingsTests
{
    private static string? None(string name) => null;

    [Test]
    public void DefaultsPointAtTheUsualUbuntuPaths()
    {
        var settings = SecSettings.Load(null, None);

        Assert.That(settings.NginxAccessLog, Is.EqualTo("/var/log/nginx/access.log"));
        Assert.That(settings.AuthLog, Is.EqualTo("/var/log/auth.log"));
        Assert.That(settings.DryRun, Is.False);
    }

    [Test]
    public void ReadsTheFileAndTheEnvironmentWins()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{ "telegramToken": "from-file", "telegramChatId": 5, "neverBlock": ["203.0.113.9"], /* note */ "modelName": "m1" }""");
            var env = new Dictionary<string, string> { ["HOMUNCULUS_SEC_TELEGRAM_TOKEN"] = "from-env", ["HOMUNCULUS_SEC_DRY_RUN"] = "true" };

            var settings = SecSettings.Load(file, name => env.GetValueOrDefault(name));

            Assert.That(settings.TelegramToken, Is.EqualTo("from-env"));
            Assert.That(settings.TelegramChatId, Is.EqualTo(5));
            Assert.That(settings.NeverBlock, Is.EqualTo(new[] { "203.0.113.9" }));
            Assert.That(settings.ModelName, Is.EqualTo("m1"));
            Assert.That(settings.DryRun, Is.True);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void RejectsABrokenFileAndABadNumber()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ nope");
            Assert.Throws<FormatException>(() => SecSettings.Load(file, None));
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Throws<FormatException>(() => SecSettings.Load(null, name => name.EndsWith("CHAT_ID", StringComparison.Ordinal) ? "abc" : null));
    }

    [Test]
    public void ListsWhatIsMissing()
    {
        Assert.That(SecSettings.Load(null, None).Problems(), Has.Count.EqualTo(2));
        Assert.That(new SecSettings { TelegramToken = "t", TelegramChatId = 1 }.Problems(), Is.Empty);
    }

    [Test]
    public void SplitsTheNeverBlockListFromTheEnvironment()
    {
        var settings = SecSettings.Load(null, name => name.EndsWith("NEVER_BLOCK", StringComparison.Ordinal) ? "1.2.3.4, 10.9.0.0/16" : null);

        Assert.That(settings.NeverBlock, Is.EqualTo(new[] { "1.2.3.4", "10.9.0.0/16" }));
    }
}
