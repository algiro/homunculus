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

var http = OpenAiModel.CreateClient(settings.BaseUrl, settings.ApiKey);

ILanguageModel ModelNamed(string name) => new OpenAiModel(http, name);

var tools = new ToolRegistry(
[
    new ListFilesTool(project),
    new ReadFileTool(project),
    new EditFileTool(project),
    new DeleteFileTool(project),
    new RunCommandTool(project, settings.CommandTimeout),
    new PlanTool(),
]);

var modes = new SwitchablePolicy(new Dictionary<string, IPolicy>
{
    ["judge"] = new JudgePolicy(ModelNamed(settings.JudgeModel)),
    ["effect"] = new FixedPolicy(Verdict.Ask),
    ["allow-all"] = new FixedPolicy(Verdict.Allow),
});
var approvals = new Approvals(new ProjectBoundaryPolicy(project, modes), ui);

var agent = new Agent(ModelNamed(settings.Model), tools, new ToolRunner(tools, approvals, ui), settings.Iterations, ui);
await new Repl(agent, modes, tools, ui).RunAsync();
return 0;
