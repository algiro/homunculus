using Homunculus.Llm;
using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus.Tests.Support;

/// <summary>The real application wired around a fake model, a fake user and a temporary project.</summary>
public sealed class TestApp : IDisposable
{
    private readonly App _app;

    public TestApp(ILanguageModel model, string mode = "allow-all", ILanguageModel? judge = null, Settings? settings = null)
    {
        Project = new TempProject();
        Ui = new FakeUserInterface();
        _app = new App(settings ?? DefaultSettings, Ui, Project.Location, model, judge ?? new ScriptedModel());
        _app.Policy.TrySetMode(mode);
    }

    public static Settings DefaultSettings { get; } = new(
        "key", "http://localhost", "model", "judge", TimeSpan.FromSeconds(30), new IterationLimits(8, 24));

    public TempProject Project { get; }
    public FakeUserInterface Ui { get; }
    public ToolRegistry Tools => _app.Tools;
    public SwitchablePolicy Policy => _app.Policy;
    public ToolRunner Runner => _app.Runner;
    public Agent Agent => _app.Agent;
    public Repl Repl => _app.Repl;

    public void Dispose() => Project.Dispose();
}
