using Homunculus.Llm;

namespace Homunculus.Tests.Support;

public sealed class FakeUserInterface : IUserInterface
{
    private readonly Queue<string> _lines = new();
    private readonly Queue<char> _choices = new();
    private Action? _onCancel;

    public List<string> Output { get; } = [];
    public List<string> Questions { get; } = [];
    public List<TokenUsage> Usages { get; } = [];
    public List<string> Reasonings { get; } = [];

    public string AllOutput => string.Join('\n', Output);

    public FakeUserInterface TypesLines(params string[] lines)
    {
        foreach (var line in lines) _lines.Enqueue(line);
        return this;
    }

    public FakeUserInterface Answers(params char[] choices)
    {
        foreach (var choice in choices) _choices.Enqueue(choice);
        return this;
    }

    public void Show(string text) => Output.Add(text);

    public void ShowReasoning(string reasoning) => Reasonings.Add(reasoning);

    public void ShowUsage(TokenUsage usage) => Usages.Add(usage);

    public char Choose(string question, string options)
    {
        Questions.Add(question);
        return _choices.Count > 0 ? _choices.Dequeue() : throw new InvalidOperationException("Unexpected question: " + question);
    }

    public string? ReadLine() => _lines.Count > 0 ? _lines.Dequeue() : null;

    public IDisposable ListenForCancel(Action onCancel)
    {
        _onCancel = onCancel;
        return new Subscription(() => _onCancel = null);
    }

    /// <summary>Simulates the user pressing ctrl-c while a turn is running.</summary>
    public void PressCancel() => _onCancel?.Invoke();
}
