using Homunculus.Llm;

namespace Homunculus.Commands;

public sealed class UsageCommand(UsageLedger ledger, IUserInterface ui) : ICommand
{
    public string Name => "/usage";
    public string Arguments => "";
    public string Description => "show the tokens used in this session";

    public CommandResult Execute(string argument)
    {
        var sources = ledger.Sources;
        if (sources.Count == 0)
        {
            ui.Show("No tokens reported yet.");
            return CommandResult.Continue;
        }

        var lines = sources.Select(Line).ToList();
        if (sources.Count > 1)
            lines.Add(Line(new SourceUsage("total", sources.Sum(s => s.Calls), sources.Aggregate(TokenUsage.None, (sum, s) => sum.Plus(s.Tokens)))));
        ui.Show(string.Join('\n', lines));
        return CommandResult.Continue;
    }

    private static string Line(SourceUsage usage) =>
        $"{usage.Source}: {usage.Calls} {(usage.Calls == 1 ? "call" : "calls")}, {UsageText.Describe(usage.Tokens)}";
}
