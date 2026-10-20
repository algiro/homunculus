namespace Homunculus;

public interface IUserInterface
{
    void Show(string text);

    /// <summary>Asks a question and returns one of the option letters (y = yes, n = no, a = always).</summary>
    char Choose(string question, string options);

    /// <summary>Reads a line of input, or null when the input has ended.</summary>
    string? ReadLine();

    /// <summary>Calls <paramref name="onCancel"/> whenever the user asks to stop; dispose to stop listening.</summary>
    IDisposable ListenForCancel(Action onCancel);
}
