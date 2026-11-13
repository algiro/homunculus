using Homunculus.Llm;

namespace Homunculus.Tests.Support;

/// <summary>The real application wired around a fake model and a fake user.</summary>
public sealed class TestApp
{
    private readonly App _app;

    public TestApp(ILanguageModel model, Settings? settings = null)
    {
        Ui = new FakeUserInterface();
        _app = new App(settings ?? DefaultSettings, Ui, model);
    }

    public static Settings DefaultSettings { get; } = new("key", "http://localhost", "model");

    public FakeUserInterface Ui { get; }
    public UsageLedger Usage => _app.Usage;
    public Agent Agent => _app.Agent;
    public Repl Repl => _app.Repl;
}
