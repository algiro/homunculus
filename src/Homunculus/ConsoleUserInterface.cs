using Homunculus.Llm;

namespace Homunculus;

public sealed class ConsoleUserInterface : IUserInterface
{
    public void Show(string text) => Console.WriteLine(text);

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
        Console.Write("> ");
        return Console.ReadLine();
    }

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
