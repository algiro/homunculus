using Homunculus.Logging;

namespace Homunculus;

/// <summary>
/// The model calls one user message may take. At the limit the user decides whether to go on; each yes grants another
/// full batch, and making a plan raises the batch size. Create one per message.
/// </summary>
public sealed class IterationBudget(IterationLimits limits, IUserInterface ui, IEventLog? log = null)
{
    private readonly IEventLog _log = log ?? NullEventLog.Instance;
    private int _extended;

    /// <summary>How many model calls have been taken so far.</summary>
    public int Used { get; private set; }

    /// <summary>Takes one more model call. False when the limit was reached and the user chose to stop.</summary>
    public bool TryTakeTurn(bool planned)
    {
        if (limits.For(planned) is { } batch && Used >= batch + _extended)
        {
            _log.Write("iteration_limit", new { iteration = Used });
            if (ui.Choose($"Reached {Used} model calls for this message. Continue for {batch} more?", "yn") != 'y')
                return false;

            _extended += batch;
        }

        Used++;
        return true;
    }
}
