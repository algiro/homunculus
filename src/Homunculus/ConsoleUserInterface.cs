using Homunculus.Llm;

namespace Homunculus;

public sealed class ConsoleUserInterface : IUserInterface
{
    public void Show(string text) => Console.WriteLine(text);

    public void ShowBanner(BannerDetails details)
    {
        if (!Console.IsOutputRedirected)
        {
            // The banner's block letters and box need UTF-8; a Windows console may still be on an old code page.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (IOException) { }
        }

        Console.WriteLine(Banner.Render(details, BannerStyleFor(Console.IsOutputRedirected, Environment.GetEnvironmentVariable), Width()));
        Console.WriteLine();
    }

    /// <summary>
    /// Plain lines when the output is not a console; no colour when NO_COLOR is set (https://no-color.org) or the terminal does
    /// not say it understands colour codes: Windows Terminal, VS Code and terminals setting COLORTERM do, and so does any Unix
    /// terminal other than "dumb". An old Windows console would print the codes as text, so it gets the banner without colour.
    /// </summary>
    public static BannerStyle BannerStyleFor(bool redirected, Func<string, string?> environment)
    {
        if (redirected) return BannerStyle.Text;
        if (!string.IsNullOrEmpty(environment("NO_COLOR"))) return BannerStyle.Art;

        var colour = OperatingSystem.IsWindows()
            ? !string.IsNullOrEmpty(environment("WT_SESSION")) || !string.IsNullOrEmpty(environment("COLORTERM"))
              || !string.IsNullOrEmpty(environment("TERM_PROGRAM")) || environment("ConEmuANSI") == "ON"
            : environment("TERM") is { Length: > 0 } term && term != "dumb";
        return colour ? BannerStyle.Color : BannerStyle.Art;
    }

    private static int Width()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch (IOException)
        {
            return 80;
        }
    }

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

    public string? ReadLine(string prompt)
    {
        // At the prompt ctrl-c has nothing to stop, so it explains how to quit instead of killing the program.
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine(QuitHint(OperatingSystem.IsWindows()));
            Console.Write(prompt);
        };

        Console.Write(prompt);
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
