using Homunculus.Sec.Detection;

namespace Homunculus.Sec.Analysis;

public enum Severity { Low, Medium, High }

/// <summary>
/// What may be done about an incident. This is a closed list on purpose: the model chooses from it and nothing else, so no
/// log line can talk it into running a command.
/// </summary>
public enum ActionKind { BlockIp, Ignore }

/// <summary>
/// The analyst's verdict on a finding. The IP to act on is always the finding's own, never something the model wrote.
/// <paramref name="FromFallback"/> is true when the model gave no usable answer and the default for the rule was used.
/// </summary>
public sealed record Proposal(
    Finding Finding,
    Severity Severity,
    ActionKind Action,
    TimeSpan BlockFor,
    string Summary,
    string Reason,
    bool FromFallback = false)
{
    public string Ip => Finding.Ip;
}
