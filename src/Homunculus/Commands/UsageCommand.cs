using Homunculus.Llm;

namespace Homunculus.Commands;

public sealed class UsageCommand(UsageLedger ledger, IUserInterface ui, TimeLedger? time = null) : ICommand
{
    public string Name => "/usage";
    public string Arguments => "";
    public string Description => "show the tokens and model time used in this session";

    public CommandResult Execute(string argument)
    {
        var sources = ledger.Sources;
        if (sources.Count == 0)
        {
            ui.Show("No tokens reported yet.");
            return CommandResult.Continue;
        }

        var lines = sources.Select(s => Line(s.Source, s.Calls, s.Tokens, time?.For(s.Source)?.Elapsed)).ToList();
        if (sources.Count > 1)
        {
            var total = sources.Aggregate(TokenUsage.None, (sum, s) => sum.Plus(s.Tokens));
            lines.Add(Line("total", sources.Sum(s => s.Calls), total, time?.Total));
        }

        ui.Show(string.Join('\n', lines));
        return CommandResult.Continue;
    }

    private static string Line(string source, int calls, TokenUsage tokens, TimeSpan? elapsed) =>
        $"{source}: {calls} {(calls == 1 ? "call" : "calls")}, {UsageText.Describe(tokens)}"
        + (elapsed is { } e ? $", {UsageText.Describe(e)}" : "");
}
