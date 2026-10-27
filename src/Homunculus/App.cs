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
        "Use the tools to inspect and change files and to run commands. List a directory before reading files in it, " +
        "and search with grep and glob to find where something is defined or used instead of guessing a path, " +
        "create a new file with write_file, and use insert_lines, not a rewritten block, to add a line next to an existing one (for example to register something), " +
        "read a file before editing it, and keep answers short. When you run tests or a build, make sure they use your latest edits. " +
        "Before you implement an interface, read it and one existing implementation. If your new code does not compile, fix your own code: " +
        "never change or delete existing files or types to make it compile. " +
        "Put each new class in its own file, named after it, and create it before you register it or use it anywhere else.";

    public App(
        Settings settings,
        IUserInterface ui,
        ProjectDirectory project,
        ILanguageModel model,
        ILanguageModel judge,
        IEventLog? log = null,
        CheckDetector? checks = null)
    {
        var allowed = new AllowedPrograms(settings.AllowedPrograms ?? AllowedPrograms.Defaults);
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
            new DeleteFileTool(project, Changes),
            new RunCommandTool(project, settings.CommandTimeout, settings.Shell),
            new PlanTool(Plan),
        ], settings.EnabledTools);

        Policy = new SwitchablePolicy(new Dictionary<string, IPolicy>
        {
            ["judge"] = new JudgePolicy(Instrument(judge, "judge")),
            ["effect"] = new FixedPolicy(Verdict.Ask),
            ["allow-all"] = new FixedPolicy(Verdict.Allow),
        });
        var approvals = new Approvals(new ProjectBoundaryPolicy(project, Policy), ui, project, allowed);

var check = CheckSelection.Choose(settings.CheckCommand, project.Root, checks ?? CheckDetector.ForThisMachine());
var guard = check.Command is { } checkCommand
    ? new ChangeGuard(
        new ProjectCheck(checkCommand, new CommandRunner(project, settings.CommandTimeout, settings.Shell), project),
        Changes, project, ui, log, settings.CheckRevert)
    : null;
Runner = new ToolRunner(Tools, approvals, ui, log, guard ?? check, project.Root);
        Agent = new Agent(Instrument(model, "agent"), new Conversation(CodingAgentPrompt(project)), Tools, Runner, settings.Iterations, settings.ShowReasoning, ui, log, Changes, Reads);

        var commands = new CommandRegistry();
        commands
            .Add(new HelpCommand(commands, ui))
            .Add(new ToolsCommand(Tools, ui))
            .Add(new PolicyCommand(Policy, ui, allowed))
            .Add(new UsageCommand(Usage, ui, Time))
            .Add(new PlanCommand(Plan, ui))
            .Add(new ClearCommand(Agent, Plan, ui))
            .Add(new ExitCommand());
    // The mode is in every prompt, so working in allow-all is never something the user has to remember.
    Repl = new Repl(Agent, commands, ui, () => $"[{Policy.Mode}] > ", () => BannerDetailsFor(settings, project, Policy.Mode));
    }

    /// <summary>The server is shown by its host, such as "localhost:11434", which says enough without the full address.</summary>
    private static BannerDetails BannerDetailsFor(Settings settings, ProjectDirectory project, string mode) => new(
        settings.Model,
        Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var server) ? server.Authority : settings.BaseUrl,
        project.Root,
        mode,
        settings.JudgeModel == settings.Model ? null : settings.JudgeModel);

    public UsageLedger Usage { get; } = new();
    public ChangedFiles Changes { get; }
    public ReadFiles Reads { get; }
    public TimeLedger Time { get; } = new();
    public Plan Plan { get; } = new();
    public ToolRegistry Tools { get; }
    public SwitchablePolicy Policy { get; }
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
