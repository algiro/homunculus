using Homunculus;
using Homunculus.Llm;
using Homunculus.Policy;
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

var ui = new ConsoleUserInterface();
var project = new ProjectDirectory(Directory.GetCurrentDirectory());

ILanguageModel ModelNamed(string name) =>
    new OpenAiModel(new HttpClient(), settings.BaseUrl, settings.ApiKey, name);

var tools = new ToolRegistry(
[
    new ListFilesTool(project),
    new ReadFileTool(project),
    new EditFileTool(project),
    new DeleteFileTool(project),
    new RunCommandTool(project, settings.CommandTimeout),
    new PlanTool(),
]);

var approvals = new Approvals(
    new ProjectBoundary(project),
    new Dictionary<string, IPolicy>
    {
        ["judge"] = new JudgePolicy(ModelNamed(settings.JudgeModel)),
        ["effect"] = new FixedPolicy(Verdict.Ask),
        ["allow-all"] = new FixedPolicy(Verdict.Allow),
    },
    ui);

var agent = new Agent(ModelNamed(settings.Model), tools, approvals, ui, settings);
await new Repl(agent, approvals, tools, ui).RunAsync();
return 0;
