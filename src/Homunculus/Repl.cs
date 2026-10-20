using Homunculus.Policy;
using Homunculus.Tools;

namespace Homunculus;

/// <summary>Reads lines from the user: slash commands are handled here, anything else goes to the agent.</summary>
public sealed class Repl(Agent agent, Approvals approvals, ToolRegistry tools, IUserInterface ui)
{
    private const string Help =
        "/help          show this help\n" +
        "/tools         list the tools\n" +
        "/policy [mode] show or set the approval mode\n" +
        "/clear         forget the conversation\n" +
        "/exit          quit\n" +
        "ctrl-c         stop the current turn";

    public async Task RunAsync()
    {
        ui.Show("homunculus - type /help for commands.");
        while (ui.ReadLine() is { } line)
        {
            var input = line.Trim();
            if (input.Length == 0) continue;
            if (input == "/exit") return;

            if (input.StartsWith('/')) HandleCommand(input);
            else await TakeTurnAsync(input);
        }
    }

    private async Task TakeTurnAsync(string input)
    {
        using var turn = new CancellationTokenSource();
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; turn.Cancel(); };
        Console.CancelKeyPress += stop;
        try
        {
            await agent.RespondAsync(input, turn.Token);
        }
        catch (OperationCanceledException)
        {
            ui.Show("Stopped.");
        }
        catch (HttpRequestException e)
        {
            ui.Show($"Error: {e.Message}");
        }
        finally
        {
            Console.CancelKeyPress -= stop;
        }
    }

    private void HandleCommand(string input)
    {
        var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
        switch (parts[0])
        {
            case "/help": ui.Show(Help); break;
            case "/tools": ui.Show(string.Join('\n', tools.All.Select(t => $"{t.Name} - {t.Description}"))); break;
            case "/clear": agent.Clear(); ui.Show("Conversation cleared."); break;
            case "/policy": SetPolicy(parts.Length > 1 ? parts[1] : null); break;
            default: ui.Show("Unknown command. Try /help."); break;
        }
    }

    private void SetPolicy(string? mode)
    {
        if (mode is not null && !approvals.TrySetMode(mode))
            ui.Show($"Unknown mode. Choose one of: {string.Join(", ", approvals.Modes)}.");
        else
            ui.Show($"Policy: {approvals.Mode} (modes: {string.Join(", ", approvals.Modes)})");
    }
}
