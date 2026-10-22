using Homunculus.Commands;
using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>The composition root: creates every part of the application and connects them.</summary>
public sealed class App
{
    private const string CodingAgentPrompt =
        "You are a coding agent working in the user's project directory. Use the tools to inspect and change files " +
        "and to run commands. List a directory before reading files in it, read a file before editing it, " +
        "and keep answers short.";

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
        ], settings.EnabledTools);

        Policy = new SwitchablePolicy(new Dictionary<string, IPolicy>
        {
            ["judge"] = new JudgePolicy(new MeteredModel(judge, Usage, "judge")),
            ["effect"] = new FixedPolicy(Verdict.Ask),
            ["allow-all"] = new FixedPolicy(Verdict.Allow),
        });
        var approvals = new Approvals(new ProjectBoundaryPolicy(project, Policy), ui);

        Runner = new ToolRunner(Tools, approvals, ui);
        Agent = new Agent(new MeteredModel(model, Usage, "agent"), new Conversation(CodingAgentPrompt), Tools, Runner, settings.Iterations, ui);

        var commands = new CommandRegistry();
        commands
            .Add(new HelpCommand(commands, ui))
            .Add(new ToolsCommand(Tools, ui))
            .Add(new PolicyCommand(Policy, ui))
            .Add(new UsageCommand(Usage, ui))
            .Add(new ClearCommand(Agent, ui))
            .Add(new ExitCommand());
        Repl = new Repl(Agent, commands, ui);
    }

    public UsageLedger Usage { get; } = new();
    public ToolRegistry Tools { get; }
    public SwitchablePolicy Policy { get; }
    public ToolRunner Runner { get; }
    public Agent Agent { get; }
    public Repl Repl { get; }
}
