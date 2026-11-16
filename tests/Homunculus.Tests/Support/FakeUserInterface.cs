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

    public void Show(string text)
    {
        NothingInProgress(text);
        Output.Add(text);
    }

    /// <summary>On a console the progress line would be written over, so nothing may be shown or asked while it is there.</summary>
    private void NothingInProgress(string what)
    {
        if (ProgressShowing > 0) throw new InvalidOperationException("Shown while progress was showing: " + what);
    }

    public List<BannerDetails> Banners { get; } = [];

    public void ShowBanner(BannerDetails details) => Banners.Add(details);

    public void ShowReasoning(string reasoning) => Reasonings.Add(reasoning);

    public void ShowUsage(TokenUsage usage) => Usages.Add(usage);

    /// <summary>Each tool call announced, as "tool summary".</summary>
    public List<string> ToolCalls { get; } = [];

    public List<ToolOutcome> ToolResults { get; } = [];

    /// <summary>Every activity shown as under way, in order.</summary>
    public List<string> Activities { get; } = [];

    /// <summary>How many progress indicators are showing now; it must be back to zero before anything is asked.</summary>
    public int ProgressShowing { get; private set; }

    public void ShowToolCall(string tool, string summary) => ToolCalls.Add($"{tool} {summary}");

    public void ShowToolResult(ToolOutcome outcome) => ToolResults.Add(outcome);

    public IDisposable ShowProgress(string activity)
    {
        Activities.Add(activity);
        ProgressShowing++;
        return new Subscription(() => ProgressShowing--);
    }

    /// <summary>The answer letters offered with each question, such as "yna", in the same order as <see cref="Questions"/>.</summary>
    public List<string> Options { get; } = [];

    public char Choose(string question, string options)
    {
        NothingInProgress(question);
        Questions.Add(question);
        Options.Add(options);
        return _choices.Count > 0 ? _choices.Dequeue() : throw new InvalidOperationException("Unexpected question: " + question);
    }

    /// <summary>Every prompt shown before a line was read, in order.</summary>
    public List<string> Prompts { get; } = [];

    public string? ReadLine(string prompt)
    {
        Prompts.Add(prompt);
        return _lines.Count > 0 ? _lines.Dequeue() : null;
    }

    public IDisposable ListenForCancel(Action onCancel)
    {
        _onCancel = onCancel;
        return new Subscription(() => _onCancel = null);
    }

    /// <summary>Simulates the user pressing ctrl-c while a turn is running.</summary>
    public void PressCancel() => _onCancel?.Invoke();
}
