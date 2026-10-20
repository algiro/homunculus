using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus.Tests.Support;

/// <summary>The real agent, tools and policies wired together around a fake model and a fake user.</summary>
public sealed class TestApp : IDisposable
{
    public TestApp(ILanguageModel model, string mode = "allow-all", ILanguageModel? judge = null, Settings? settings = null)
    {
        Project = new TempProject();
        Ui = new FakeUserInterface();
        Settings = settings ?? DefaultSettings;
        Tools = new ToolRegistry(
        [
            new ListFilesTool(Project.Location),
            new ReadFileTool(Project.Location),
            new EditFileTool(Project.Location),
            new DeleteFileTool(Project.Location),
            new RunCommandTool(Project.Location, Settings.CommandTimeout),
            new PlanTool(),
        ]);
        Policy = new SwitchablePolicy(new Dictionary<string, IPolicy>
        {
            [mode] = ModeFor(mode, judge),
            ["effect"] = new FixedPolicy(Verdict.Ask),
            ["allow-all"] = new FixedPolicy(Verdict.Allow),
        });
        Approvals = new Approvals(new ProjectBoundaryPolicy(Project.Location, Policy), Ui);
        Agent = new Agent(model, Tools, new ToolRunner(Tools, Approvals, Ui), Settings.Iterations, Ui);
    }

    public static Settings DefaultSettings { get; } = new(
        "key", "http://localhost", "model", "judge", TimeSpan.FromSeconds(30), new IterationLimits(8, 24));

    public TempProject Project { get; }
    public FakeUserInterface Ui { get; }
    public Settings Settings { get; }
    public ToolRegistry Tools { get; }
    public SwitchablePolicy Policy { get; }
    public Approvals Approvals { get; }
    public Agent Agent { get; }

    public Repl NewRepl() => new(Agent, Policy, Tools, Ui);

    public void Dispose() => Project.Dispose();

    private static IPolicy ModeFor(string mode, ILanguageModel? judge) => mode switch
    {
        "judge" => new JudgePolicy(judge ?? throw new ArgumentException("A judge model is required.")),
        "effect" => new FixedPolicy(Verdict.Ask),
        _ => new FixedPolicy(Verdict.Allow),
    };
}
