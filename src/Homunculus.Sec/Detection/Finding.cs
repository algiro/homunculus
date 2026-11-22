namespace Homunculus.Sec.Detection;

/// <summary>
/// A rule fired: <paramref name="Count"/> matching events (or, for the web scanner rule, a suspicion score) from
/// <paramref name="Ip"/> within <paramref name="Window"/>. <paramref name="Summary"/> is text the detector composed for the analyst,
/// when the evidence is more than a few log lines. <paramref name="Hard"/> is true when the evidence includes something no ordinary
/// visitor produces (injection strings, paths only scanners ask for, unusual methods), which the analyst cannot argue away.
/// <paramref name="Caution"/> is a warning the operator should read before approving (for a network: that it would also cut off an
/// address that logged in successfully). For a network finding <paramref name="Ip"/> holds the network, such as <c>203.0.113.0/24</c>.
/// </summary>
public sealed record Finding(
    string Rule,
    string Ip,
    DateTimeOffset Time,
    int Count,
    TimeSpan Window,
    IReadOnlyList<string> Samples,
    string? Summary = null,
    bool Hard = false,
    string? Caution = null);
