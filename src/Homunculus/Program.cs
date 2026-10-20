using Homunculus;
using Homunculus.Llm;
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

var http = OpenAiModel.CreateClient(settings.BaseUrl, settings.ApiKey);
var app = new App(
    settings,
    new ConsoleUserInterface(),
    new ProjectDirectory(Directory.GetCurrentDirectory()),
    new OpenAiModel(http, settings.Model),
    new OpenAiModel(http, settings.JudgeModel));

await app.Repl.RunAsync();
return 0;
