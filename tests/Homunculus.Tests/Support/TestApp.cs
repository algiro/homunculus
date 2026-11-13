using Homunculus.Llm;
using Homunculus.Tools;

namespace Homunculus.Tests.Support;

/// <summary>The real application wired around a fake model, a fake user and a temporary project.</summary>
public sealed class TestApp : IDisposable
{
    private readonly App _app;

    public TestApp(ILanguageModel model, Settings? settings = null, IEnumerable<string>? projectFiles = null)
    {
        Project = new TempProject();
        foreach (var file in projectFiles ?? []) Project.Write(file, "");
        Ui = new FakeUserInterface();
        _app = new App(settings ?? DefaultSettings, Ui, Project.Location, model);
    }

    public static Settings DefaultSettings { get; } = new("key", "http://localhost", "model", new IterationLimits(8));

    public TempProject Project { get; }
    public FakeUserInterface Ui { get; }
    public UsageLedger Usage => _app.Usage;
    public ToolRegistry Tools => _app.Tools;
    public ToolRunner Runner => _app.Runner;
    public Agent Agent => _app.Agent;
    public Repl Repl => _app.Repl;

    public void Dispose() => Project.Dispose();
}
