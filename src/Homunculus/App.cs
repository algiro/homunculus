using Homunculus.Commands;
using Homunculus.Llm;
using Homunculus.Logging;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>The composition root: creates every part of the application and connects them.</summary>
public sealed class App
{
    private static string CodingAgentPrompt(ProjectDirectory project) =>
        $"You are a coding agent working in the project directory {project.Root}. Use paths relative to it " +
        "(for example '.' is the project root); reaching anything outside it needs the user's approval each time. " +
        "Use the tools to inspect and change files. List a directory before reading files in it, " +
        "and search with grep and glob to find where something is defined or used instead of guessing a path, " +
        "create a new file with write_file, and use insert_lines, not a rewritten block, to add a line next to an existing one (for example to register something), " +
        "read a file before editing it, and keep answers short. " +
        "Before you implement an interface, read it and one existing implementation. " +
        "Never change or delete existing files or types just to make your own code fit. " +
        "Put each new class in its own file, named after it, and create it before you register it or use it anywhere else.";

    public App(
        Settings settings,
        IUserInterface ui,
        ProjectDirectory project,
        ILanguageModel model,
        IEventLog? log = null)
    {
        Changes = new ChangedFiles(project);
        Reads = new ReadFiles();
        Tools = new ToolRegistry(
        [
            new ListFilesTool(project),
            new GlobTool(project),
            new GrepTool(project),
            new ReadFileTool(project, Reads),
            new EditFileTool(project, Changes, Reads),
            new WriteFileTool(project, Changes, Reads),
            new InsertLinesTool(project, Changes),
        ], settings.EnabledTools);

        // Reads and writes inside the project run at once; anything else is put to the user.
        var approvals = new Approvals(new ProjectBoundaryPolicy(project, new FixedPolicy(Verdict.Ask)), ui, project);
        Runner = new ToolRunner(Tools, approvals, ui, log);
        Agent = new Agent(Instrument(model, "agent"), new Conversation(CodingAgentPrompt(project)), Tools, Runner, settings.Iterations, settings.ShowReasoning, ui, log, Changes, Reads);

        var commands = new CommandRegistry();
        commands
            .Add(new HelpCommand(commands, ui))
            .Add(new ToolsCommand(Tools, ui))
            .Add(new UsageCommand(Usage, ui, Time))
            .Add(new ClearCommand(Agent, ui))
            .Add(new ExitCommand());
        Repl = new Repl(Agent, commands, ui);
    }

    public UsageLedger Usage { get; } = new();
    public ChangedFiles Changes { get; }
    public ReadFiles Reads { get; }
    public TimeLedger Time { get; } = new();
    public ToolRegistry Tools { get; }
    public ToolRunner Runner { get; }
    public Agent Agent { get; }
    public Repl Repl { get; }

    /// <summary>
    /// Decorators nest like onion layers: a call goes in through the outer one and comes back out through it.
    /// Time is the inner layer so it measures the model alone; the token ledger sits around it.
    /// </summary>
    private MeteredModel Instrument(ILanguageModel model, string source) =>
        new MeteredModel(new TimedModel(model, Time, source), Usage, source);
}
