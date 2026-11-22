using System.Globalization;
using System.Net;
using System.Text;
using Homunculus.Sec.Analysis;
using Homunculus.Sec.Safety;
using Homunculus.Sec.Telegram;

namespace Homunculus.Sec.Service;

/// <summary>Builds the Telegram texts (HTML mode). Everything that came from a log or the model is escaped.</summary>
public static class AlertFormatter
{
    public static string Alert(Incident incident)
    {
        var p = incident.Proposal;
        var icon = p.Severity switch { Severity.High => "🔴", Severity.Medium => "🟠", _ => "🟡" };
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{icon} <b>{p.Severity.ToString().ToUpperInvariant()}</b> · {Escape(p.Finding.Rule)} · #{incident.Id}");
        var network = Cidr.IsNetwork(p.Ip);
        text.AppendLine(CultureInfo.InvariantCulture, $"{(network ? "Network" : "IP")}: <code>{Escape(p.Ip)}</code>");
        text.AppendLine(p.Finding.Summary is { } summary
            ? "Evidence: " + Escape(summary.Split('\n')[0])
            : string.Create(CultureInfo.InvariantCulture, $"Evidence: {p.Finding.Count} events in {p.Finding.Window.TotalSeconds:0}s"));
        if (p.Finding.Caution is { } caution) text.AppendLine("⚠️ " + Escape(caution));
        text.AppendLine();
        text.AppendLine(Escape(p.Summary));
        text.AppendLine(Escape(p.Reason));
        if (p.FromFallback) text.AppendLine("<i>The model was unavailable; this is the default recommendation.</i>");
        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"<b>Suggested:</b> block {(network ? "the whole network " : "")}<code>{Escape(p.Ip)}</code> for {Duration(p.BlockFor)}.");
        text.Append("\nReply yes / no, press a button, or ask me a question.");
        return text.ToString();
    }

    public static IReadOnlyList<Button> Buttons(Incident incident) =>
    [
        new Button($"✅ Block {(Cidr.IsNetwork(incident.Proposal.Ip) ? "range " : "")}{Duration(incident.Proposal.BlockFor)}", $"a:{incident.Token}"),
        new Button("🚫 Ignore", $"i:{incident.Token}"),
    ];

    public static string Duration(TimeSpan span) =>
        span.TotalHours >= 48 ? $"{span.TotalDays:0}d" : span.TotalMinutes >= 120 ? $"{span.TotalHours:0}h" : $"{span.TotalMinutes:0}min";

    public static string Escape(string text) => WebUtility.HtmlEncode(text);
}
