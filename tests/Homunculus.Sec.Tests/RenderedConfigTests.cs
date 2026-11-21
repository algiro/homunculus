namespace Homunculus.Sec.Tests;

public class RenderedConfigTests
{
    [Test]
    public void TheConfigurationWrittenBySetupRootShLoads()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deploy", "setup-root.sh"))) dir = dir.Parent;
        Assert.That(dir, Is.Not.Null);

        var script = File.ReadAllText(Path.Combine(dir!.FullName, "deploy", "setup-root.sh"));
        var start = script.IndexOf("<<EOF\n", script.IndexOf("config.json\" <<EOF", StringComparison.Ordinal) - 2, StringComparison.Ordinal) + "<<EOF\n".Length;
        var template = script[start..script.IndexOf("\nEOF\n", start, StringComparison.Ordinal)]
            .Replace("$ACCESS_LOG", "/var/log/homunculus-sec/nginx.log", StringComparison.Ordinal)
            .Replace("$ERROR_LOG", "/var/log/homunculus-sec/nginx.log", StringComparison.Ordinal)
            .Replace("$CHAT_ID", "123456", StringComparison.Ordinal)
            .Replace("$MODEL", "qwen2.5:3b", StringComparison.Ordinal)
            .Replace("$FIREWALL", "iptables", StringComparison.Ordinal)
            .Replace("$STATE_DIR", "/var/lib/homunculus-sec", StringComparison.Ordinal);
        Assert.That(template, Does.Not.Contain("$"));

        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, template);
            var settings = SecSettings.Load(file, _ => null);

            Assert.That(settings.DryRun, Is.True);
            Assert.That(settings.Firewall, Is.EqualTo("iptables"));
            Assert.That(settings.TelegramChatId, Is.EqualTo(123456));
            Assert.That(settings.NginxAccessLog, Is.EqualTo("/var/log/homunculus-sec/nginx.log"));
            Assert.That(settings.StatePath, Is.EqualTo("/var/lib/homunculus-sec/blocks.json"));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
