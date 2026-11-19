using System.Globalization;
using System.Net;
using System.Text;
using Homunculus.Sec.Analysis;
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
        text.AppendLine(CultureInfo.InvariantCulture, $"IP: <code>{Escape(p.Ip)}</code>");
        text.AppendLine(CultureInfo.InvariantCulture, $"Evidence: {p.Finding.Count} events in {p.Finding.Window.TotalSeconds:0}s");
        text.AppendLine();
        text.AppendLine(Escape(p.Summary));
        text.AppendLine(Escape(p.Reason));
        if (p.FromFallback) text.AppendLine("<i>The model was unavailable; this is the default recommendation.</i>");
        text.AppendLine();
        text.Append(CultureInfo.InvariantCulture, $"<b>Suggested:</b> block <code>{Escape(p.Ip)}</code> for {Duration(p.BlockFor)} (ufw deny).");
        text.Append("\nReply yes / no, press a button, or ask me a question.");
        return text.ToString();
    }

    public static IReadOnlyList<Button> Buttons(Incident incident) =>
    [
        new Button($"✅ Block {Duration(incident.Proposal.BlockFor)}", $"a:{incident.Id}"),
        new Button("🚫 Ignore", $"i:{incident.Id}"),
    ];

    public static string Duration(TimeSpan span) =>
        span.TotalHours >= 48 ? $"{span.TotalDays:0}d" : span.TotalMinutes >= 120 ? $"{span.TotalHours:0}h" : $"{span.TotalMinutes:0}min";

    public static string Escape(string text) => WebUtility.HtmlEncode(text);
}
