using Homunculus.Tools;

namespace Homunculus.Tests.Support;

/// <summary>A command runner that answers with a canned outcome (or failure) and remembers what it was asked to run.</summary>
public sealed class FakeCommandRunner : ICommandRunner
{
    public Shell Shell { get; } = new("fake-shell", "fake", []);

    public CommandOutcome Outcome { get; set; } = new("", 0);
    public Exception? Failure { get; set; }
    public List<string> Commands { get; } = [];

    public Task<CommandOutcome> RunAsync(string command, CancellationToken cancellation)
    {
        Commands.Add(command);
        return Failure is null ? Task.FromResult(Outcome) : throw Failure;
    }
}
