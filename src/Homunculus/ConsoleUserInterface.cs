using Homunculus.Llm;

namespace Homunculus;

public sealed class ConsoleUserInterface : IUserInterface
{
    public void Show(string text) => Console.WriteLine(text);

    public void ShowReasoning(string reasoning)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        try
        {
            Console.WriteLine(PrefixLines(reasoning));
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }

    /// <summary>Reasoning is often several paragraphs; every line carries the prefix so none reads like part of the answer.</summary>
    public static string PrefixLines(string reasoning) =>
        string.Join(Environment.NewLine, reasoning.ReplaceLineEndings("\n").Split('\n').Select(line => $"[thinking] {line}".TrimEnd()));

    public void ShowUsage(TokenUsage usage) => Console.WriteLine($"[tokens: {UsageText.Describe(usage)}]");

    public char Choose(string question, string options)
    {
        while (true)
        {
            Console.Write($"{question} [{string.Join('/', options)}] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer is null) return 'n';
            if (answer.Length == 1 && options.Contains(answer[0])) return answer[0];
        }
    }

    public string? ReadLine()
    {
        // At the prompt ctrl-c has nothing to stop, so it explains how to quit instead of killing the program.
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine(QuitHint(OperatingSystem.IsWindows()));
            Console.Write("> ");
        };

        Console.Write("> ");
        Console.CancelKeyPress += handler;
        try
        {
            return Console.ReadLine();
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    /// <summary>The Windows console does not treat ctrl-d as the end of input; ctrl-z then Enter is its equivalent.</summary>
    public static string QuitHint(bool windows) =>
        windows ? "Use /exit or Ctrl-Z then Enter to quit." : "Use /exit or Ctrl-D to quit.";

    public IDisposable ListenForCancel(Action onCancel)
    {
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            onCancel();
        };
        Console.CancelKeyPress += handler;
        return new Subscription(() => Console.CancelKeyPress -= handler);
    }
}
