using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Sec.Analysis;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Firewall;
using Homunculus.Sec.Logs;
using Homunculus.Sec.Safety;
using Homunculus.Sec.Service;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec;

public static class Program
{
    private const string DefaultConfig = "/etc/homunculus-sec/config.json";

    public static async Task<int> Main(string[] args)
    {
        var path = args.Length > 0 ? args[0] : File.Exists(DefaultConfig) ? DefaultConfig : File.Exists("homunculus-sec.json") ? "homunculus-sec.json" : null;

        SecSettings settings;
        try
        {
            settings = SecSettings.Load(path, Environment.GetEnvironmentVariable);
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }

        if (settings.Problems() is { Count: > 0 } problems)
        {
            foreach (var problem in problems) Console.Error.WriteLine(problem);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        using var terminate = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM, _ => cancellation.Cancel());

        using var eventLog = settings.EventLogPath is null ? null : new JsonlEventLog(settings.EventLogPath);
        var monitor = Build(settings, eventLog ?? (IEventLog)NullEventLog.Instance);

        Console.WriteLine($"homunculus-sec running{(settings.DryRun ? " (dry run: no firewall changes)" : "")}. Stop with ctrl-c.");
        await monitor.RunAsync(cancellation.Token);
        Console.WriteLine("homunculus-sec stopped.");
        return 0;
    }

    private static SecMonitor Build(SecSettings settings, IEventLog log)
    {
        var allowlist = new Allowlist(settings.NeverBlock);
        var guard = new ProposalGuard(allowlist, TimeSpan.FromHours(settings.MaxBlockHours));

        IFirewall firewall = settings.DryRun ? new DryRunFirewall() : new UfwFirewall(new ProcessRunner());
        var blocks = new BlockManager(firewall, settings.StatePath, log: log);

        var model = new OpenAiModel(OpenAiModel.CreateClient(settings.ModelBaseUrl, settings.ModelApiKey), settings.ModelName, log);
        var analyst = new Analyst(model, log);
        var telegram = new TelegramApi(TelegramApi.CreateClient(), settings.TelegramToken);
        var book = new IncidentBook();
        var chat = new ChatHandler(telegram, settings.TelegramChatId, book, blocks, analyst, allowlist, log);

        WatchedLog[] logs =
        [
            new(new LogTailer(settings.NginxAccessLog), new NginxAccessParser()),
            new(new LogTailer(settings.NginxErrorLog), new NginxErrorParser()),
            new(new LogTailer(settings.AuthLog), new AuthLogParser()),
        ];

        return new SecMonitor(logs, DefaultDetectors.Create(), analyst, guard, book, telegram, settings.TelegramChatId, blocks, chat, log);
    }
}
