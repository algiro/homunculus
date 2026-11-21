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

    [Test]
    public void TheFirewallDefaultsToIptablesAndCanBeChosenByEnvironmentButOnlyFromTheKnownOnes()
    {
        Assert.That(SecSettings.Load(null, None).Firewall, Is.EqualTo("iptables"));
        Assert.That(SecSettings.Load(null, name => name.EndsWith("FIREWALL", StringComparison.Ordinal) ? "UFW" : null).Firewall, Is.EqualTo("ufw"));
        Assert.That((new SecSettings { TelegramToken = "t", TelegramChatId = 1, Firewall = "nft" }).Problems(), Has.Count.EqualTo(1));
    }

    [Test]
    public void TheShippedExampleConfigurationIsValid()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deploy", "config.example.json"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "deploy/config.example.json not found");

        var settings = SecSettings.Load(Path.Combine(dir!.FullName, "deploy", "config.example.json"), None);

        Assert.That(settings.TelegramChatId, Is.Not.Zero);
        Assert.That(settings.DryRun, Is.True);
        Assert.That(new Homunculus.Sec.Safety.Allowlist(settings.NeverBlock).IsProtected("198.51.100.77"), Is.True);
    }
}
