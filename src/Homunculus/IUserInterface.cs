namespace Homunculus;

public interface IUserInterface
{
    void Show(string text);

    /// <summary>Shows the start-up banner, as rich as the output allows.</summary>
    void ShowBanner(BannerDetails details);

    /// <summary>Shows the thinking a reasoning model reported before its answer or tool call.</summary>
    void ShowReasoning(string reasoning);

    /// <summary>Shows the model's answer, which is usually markdown.</summary>
    void ShowAnswer(string markdown);

    /// <summary>Shows what the turn that just ended used, on one line.</summary>
    void ShowTurnSummary(TurnSummary turn);

    /// <summary>Shows that a tool is about to run, by what it works on, such as "src/App.cs" or "dotnet test".</summary>
    void ShowToolCall(string tool, string summary);

    /// <summary>Shows how the tool call announced last ended.</summary>
    void ShowToolResult(ToolOutcome outcome);

    /// <summary>Shows that something is under way, such as "thinking", until the result is disposed.</summary>
    IDisposable ShowProgress(string activity);

    /// <summary>Asks a question and returns one of the option letters (y = yes, n = no, a = always).</summary>
    char Choose(string question, string options);

    /// <summary>Shows <paramref name="prompt"/> and reads a line of input, or null when the input has ended.</summary>
    string? ReadLine(string prompt);

    /// <summary>Calls <paramref name="onCancel"/> whenever the user asks to stop; dispose to stop listening.</summary>
    IDisposable ListenForCancel(Action onCancel);
}
