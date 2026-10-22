using Homunculus;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Tools;

Settings settings;
try
{
    settings = Settings.FromEnvironment();
}
catch (InvalidOperationException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

using var fileLog = settings.LogFile is { } logFile ? new JsonlEventLog(logFile) : null;
IEventLog log = fileLog ?? (IEventLog)NullEventLog.Instance;

App app;
try
{
    var http = OpenAiModel.CreateClient(settings.BaseUrl, settings.ApiKey);
    app = new App(
        settings,
        new ConsoleUserInterface(),
        new ProjectDirectory(Directory.GetCurrentDirectory()),
        new OpenAiModel(http, settings.Model, log),
        new OpenAiModel(http, settings.JudgeModel, log),
        log);
}
catch (InvalidOperationException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

await app.Repl.RunAsync();
return 0;
