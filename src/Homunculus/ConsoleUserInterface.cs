using System.Diagnostics;
using Homunculus.Llm;

namespace Homunculus;

public sealed class ConsoleUserInterface : IUserInterface
{
    /// <summary>How much this console can draw, worked out once: the banner, the tool lines and the progress all follow it.</summary>
    private readonly BannerStyle _style;

    public ConsoleUserInterface()
    {
        _style = BannerStyleFor(Console.IsOutputRedirected, Environment.GetEnvironmentVariable);
        if (_style != BannerStyle.Text)
        {
            // Box drawing, block letters and the progress frames need UTF-8; a Windows console may still be on an old code page.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
            catch (IOException) { }
        }
    }

    public void Show(string text) => Console.WriteLine(text);

    public void ShowBanner(BannerDetails details)
    {
        Console.WriteLine(Banner.Render(details, _style, Width()));
        Console.WriteLine();
    }

    public void ShowToolCall(string tool, string summary) => Console.WriteLine(ToolCallLine(tool, summary, _style));

    public void ShowToolResult(ToolOutcome outcome) => Console.WriteLine(ToolResultLine(outcome, _style));

    private const string Reset = "\e[0m";
    private const string Bold = "\e[1m";
    private const string Green = "\e[38;2;95;207;159m";
    private const string Grey = "\e[38;2;138;131;153m";
    private const string Red = "\e[38;2;239;108;108m";

    /// <summary>"● read_file  src/App.cs": a dot and the tool's name, then what it works on.</summary>
    public static string ToolCallLine(string tool, string summary, BannerStyle style)
    {
        var gap = summary.Length == 0 ? "" : "  ";
        return style switch
        {
            BannerStyle.Color => $"{Green}●{Reset} {Bold}{tool}{Reset}{gap}{summary}",
            BannerStyle.Art => $"● {tool}{gap}{summary}",
            _ => $"-> {tool}{gap}{summary}",
        };
    }

    /// <summary>"  ⎿  12 lines · 1.3s" under the call: grey when it went well, red when it failed.</summary>
    public static string ToolResultLine(ToolOutcome outcome, BannerStyle style)
    {
        var text = ToolDisplay.Duration(outcome.Elapsed) is { } duration ? $"{outcome.Summary} · {duration}" : outcome.Summary;
        return style switch
        {
            BannerStyle.Color => $"  {Grey}⎿{Reset}  {(outcome.Failed ? Red : Grey)}{text}{Reset}",
            BannerStyle.Art => $"  └  {text}",
            _ => $"   {(outcome.Failed ? "!" : "=")} {text}",
        };
    }

    /// <summary>
    /// A spinner with the activity and the seconds so far, drawn over itself on one line and wiped when disposed. It waits a
    /// moment before it appears, so quick steps do not flicker. Output that is not a console gets nothing.
    /// </summary>
    public IDisposable ShowProgress(string activity)
    {
        if (_style == BannerStyle.Text) return new Subscription(() => { });

        var stop = new CancellationTokenSource();
        var started = Stopwatch.GetTimestamp();
        var drawn = false;
        var spinner = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stop.Token);
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                for (var frame = 0; !stop.IsCancellationRequested; frame++)
                {
                    lock (_console)
                    {
                        if (stop.IsCancellationRequested) break;
                        Console.Write(ProgressLine(activity, frame, Stopwatch.GetElapsedTime(started), _style));
                        drawn = true;
                    }

                    await timer.WaitForNextTickAsync(stop.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        return new Subscription(() =>
        {
            lock (_console)
            {
                stop.Cancel();
                if (drawn) Console.Write(_style == BannerStyle.Color ? "\r\e[2K\e[?25h" : "\r" + new string(' ', Width() - 1) + "\r");
            }

            spinner.Wait(TimeSpan.FromSeconds(1));
            stop.Dispose();
        });
    }

    private readonly Lock _console = new();

    private static readonly string[] ColourFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly string[] PlainFrames = ["|", "/", "-", "\\"];

    /// <summary>One frame of the progress line, starting with a carriage return so the next frame draws over it.</summary>
    public static string ProgressLine(string activity, int frame, TimeSpan elapsed, BannerStyle style)
    {
        var seconds = elapsed >= TimeSpan.FromSeconds(1) ? $" {(int)elapsed.TotalSeconds}s" : "";
        return style == BannerStyle.Color
            ? $"\r\e[?25l{Green}{ColourFrames[frame % ColourFrames.Length]}{Reset} {Grey}{activity}…{seconds}{Reset}\e[K"
            : $"\r{PlainFrames[frame % PlainFrames.Length]} {activity}...{seconds}   ";
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
