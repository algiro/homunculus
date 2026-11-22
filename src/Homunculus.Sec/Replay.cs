using System.Globalization;
using System.Text;
using Homunculus.Sec.Detection;
using Homunculus.Sec.Logs;

namespace Homunculus.Sec;

/// <summary>
/// Feeds an existing log through the detectors and lists what they would have reported: no Telegram, no model, no firewall.
/// For tuning the thresholds on real traffic (<c>homunculus-sec --replay FILE</c>). Lines of any of the three supported logs may be mixed.
/// </summary>
public static class Replay
{
    private static readonly ILogParser[] Parsers = [new NginxAccessParser(), new NginxErrorParser(), new AuthLogParser()];

    public static IReadOnlyList<Finding> Run(IEnumerable<string> lines, DetectorOptions? options = null)
    {
        var detectors = DefaultDetectors.Create(options);
        var findings = new List<Finding>();
        foreach (var line in lines)
        {
            var entry = Parsers.Select(parser => parser.Parse(line)).FirstOrDefault(e => e is not null);
            if (entry is null) continue;
            foreach (var detector in detectors)
                if (detector.Observe(entry) is { } finding)
                    findings.Add(finding);
        }

        return findings;
    }

    /// <summary>One block of text per finding, plus a count of findings per rule.</summary>
    public static string Describe(IReadOnlyList<Finding> findings, bool showSummaries)
    {
        var text = new StringBuilder();
        foreach (var finding in findings)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{finding.Time:yyyy-MM-dd HH:mm:ss}Z  {finding.Rule,-17} {finding.Ip}  count/score {finding.Count}{(finding.Hard ? "  [hard evidence]" : "")}");
            if (showSummaries && finding.Summary is { } summary)
                foreach (var line in summary.Split('\n')) text.AppendLine("    " + line.TrimEnd());
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{findings.Count} finding(s) from {findings.Select(f => f.Ip).Distinct().Count()} address(es):");
        foreach (var group in findings.GroupBy(f => f.Rule).OrderByDescending(g => g.Count()))
            text.AppendLine(CultureInfo.InvariantCulture, $"  {group.Key,-17} {group.Count(),4} finding(s), {group.Select(f => f.Ip).Distinct().Count()} address(es)");
        return text.ToString();
    }
}
