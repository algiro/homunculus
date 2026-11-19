namespace Homunculus.Sec.Detection;

/// <summary>A rule fired: <paramref name="Count"/> matching events from <paramref name="Ip"/> within <paramref name="Window"/>.</summary>
public sealed record Finding(string Rule, string Ip, DateTimeOffset Time, int Count, TimeSpan Window, IReadOnlyList<string> Samples);
