using Homunculus.Commands;
using Homunculus.Llm;
using Homunculus.Logging;

namespace Homunculus;

/// <summary>The composition root: creates every part of the application and connects them.</summary>
public sealed class App
{
    private static string AssistantPrompt() =>
        "You are a helpful assistant. Answer the user's questions clearly and keep answers short. " +
        "You cannot see the user's files or the internet, so say so when a question needs them. " +
        $"Today is {DateTime.Now:dddd, d MMMM yyyy}.";

    public App(
        Settings settings,
        IUserInterface ui,
        ILanguageModel model,
        IEventLog? log = null)
    {
        Agent = new Agent(Instrument(model, "agent"), new Conversation(AssistantPrompt()), settings.ShowReasoning, ui, log);

        var commands = new CommandRegistry();
        commands
            .Add(new HelpCommand(commands, ui))
            .Add(new UsageCommand(Usage, ui, Time))
            .Add(new ClearCommand(Agent, ui))
            .Add(new ExitCommand());
        Repl = new Repl(Agent, commands, ui);
    }

    public UsageLedger Usage { get; } = new();
    public TimeLedger Time { get; } = new();
    public Agent Agent { get; }
    public Repl Repl { get; }

    /// <summary>
    /// Decorators nest like onion layers: a call goes in through the outer one and comes back out through it.
    /// Time is the inner layer so it measures the model alone; the token ledger sits around it.
    /// </summary>
    private MeteredModel Instrument(ILanguageModel model, string source) =>
        new MeteredModel(new TimedModel(model, Time, source), Usage, source);
}
