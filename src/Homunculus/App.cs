using Homunculus.Commands;
using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>The composition root: creates every part of the application and connects them.</summary>
public sealed class App
{
    public App(Settings settings, IUserInterface ui, ProjectDirectory project, ILanguageModel model, ILanguageModel judge)
    {
        Tools = new ToolRegistry(
        [
            new ListFilesTool(project),
            new ReadFileTool(project),
            new EditFileTool(project),
            new DeleteFileTool(project),
            new RunCommandTool(project, settings.CommandTimeout),
            new PlanTool(),
        ]);

        Policy = new SwitchablePolicy(new Dictionary<string, IPolicy>
        {
            ["judge"] = new JudgePolicy(judge),
            ["effect"] = new FixedPolicy(Verdict.Ask),
            ["allow-all"] = new FixedPolicy(Verdict.Allow),
        });
        var approvals = new Approvals(new ProjectBoundaryPolicy(project, Policy), ui);

        Runner = new ToolRunner(Tools, approvals, ui);
        Agent = new Agent(model, Tools, Runner, settings.Iterations, ui);

        var commands = new CommandRegistry();
        commands
            .Add(new HelpCommand(commands, ui))
            .Add(new ToolsCommand(Tools, ui))
            .Add(new PolicyCommand(Policy, ui))
            .Add(new ClearCommand(Agent, ui))
            .Add(new ExitCommand());
        Repl = new Repl(Agent, commands, ui);
    }

    public ToolRegistry Tools { get; }
    public SwitchablePolicy Policy { get; }
    public ToolRunner Runner { get; }
    public Agent Agent { get; }
    public Repl Repl { get; }
}
