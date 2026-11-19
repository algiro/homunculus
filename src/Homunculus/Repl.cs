using Homunculus.Commands;
using Homunculus.Llm;

namespace Homunculus;

/// <summary>
/// Reads lines from the user: slash commands go to the command registry, anything else goes to the agent. It opens with the
/// banner when given what to put in it, then the start-up note, if any.
/// </summary>
public sealed class Repl(
    Agent agent,
    CommandRegistry commands,
    IUserInterface ui,
    Func<string>? prompt = null,
    Func<BannerDetails>? banner = null,
    string? startupNote = null)
{
    public async Task RunAsync()
    {
        if (banner is not null) ui.ShowBanner(banner());
        else ui.Show("homunculus - type /help for commands.");
        if (startupNote is not null) ui.Show(startupNote);
        while (ui.ReadLine(prompt?.Invoke() ?? "> ") is { } line)
        {
            var input = line.Trim();
            if (input.Length == 0) continue;

            if (!input.StartsWith('/'))
                await TakeTurnAsync(input);
            else if (Execute(input) == CommandResult.Exit)
                return;
        }
    }

    private CommandResult Execute(string input)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
        var command = commands.Find(parts[0]);
        if (command is null)
        {
            ui.Show("Unknown command. Try /help.");
            return CommandResult.Continue;
        }

        return command.Execute(parts.Length > 1 ? parts[1] : "");
    }

    private async Task TakeTurnAsync(string input)
    {
        using var turn = new CancellationTokenSource();
        using var listening = ui.ListenForCancel(turn.Cancel);
        try
        {
            await agent.RespondAsync(input, turn.Token);
        }
        catch (OperationCanceledException)
        {
            ui.Show("Stopped.");
        }
        catch (ModelException e)
        {
            ui.Show($"Error: {e.Message}");
        }
    }
}
