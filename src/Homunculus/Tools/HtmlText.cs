using System.Net;
using System.Text.RegularExpressions;

namespace Homunculus.Tools;

/// <summary>
/// Turns a web page into the plain text a small model can read: no scripts, styles, menus or markup. A table keeps one
/// line per row with its cells separated by " | ", so that a forecast or price list still reads as rows and columns.
/// </summary>
public static partial class HtmlText
{
    private const string CellSeparator = " | ";

    [GeneratedRegex(@"<!--.*?-->|<(head|script|style|noscript|svg|nav|header|footer|form|iframe|template)\b.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"<title[^>]*>(?<title>.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Title();

    /// <summary>The page's own content, when it marks it; everything outside is menus, banners and footers.</summary>
    [GeneratedRegex(@"<(main|article)\b[^>]*>(?<content>.*)</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MainContent();

    /// <summary>A list item that holds nothing but one link: a menu entry, which buries the content of most pages.</summary>
    [GeneratedRegex(@"<li\b[^>]*>\s*<a\b[^>]*>(?:(?!</?(?:a|li|ul|ol)\b).)*</a>\s*(?:</li\s*>)?", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MenuEntry();

    [GeneratedRegex(@"<table\b.*?</table\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Table();

    [GeneratedRegex(@"<tr\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Row();

    [GeneratedRegex(@"<t[dh]\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Cell();

    /// <summary>An image's alt text, which is often all a table cell says (for example a weather icon's "Fog").</summary>
    [GeneratedRegex(@"<img\b[^>]*?\balt\s*=\s*""(?<alt>[^""]*)""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageAlt();

    [GeneratedRegex(@"</?(p|div|br|li|ul|ol|tr|table|h[1-6]|section|article|main|pre|blockquote)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Block();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\f\v ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@" ?\n ?(\n ?)+")]
    private static partial Regex BlankLines();

    public static string From(string html)
    {
        var title = Title().Match(html) is { Success: true } match ? Clean(match.Groups["title"].Value) : "";

        var body = Noise().Replace(html, " ");
        if (MainContent().Match(body) is { Success: true } main && Tag().Replace(main.Groups["content"].Value, "").Trim().Length > 0)
            body = main.Groups["content"].Value;
        body = MenuEntry().Replace(body, " ");
        body = Table().Replace(body, table => "\n" + TableText(table.Value) + "\n");
        body = Block().Replace(body, "\n");
        body = Tag().Replace(body, " ");
        body = WebUtility.HtmlDecode(body);
        body = Spaces().Replace(body, " ");
        body = BlankLines().Replace(body.Replace("\r", ""), "\n\n").Trim();

        return title.Length > 0 ? $"{title}\n\n{body}" : body;
    }

    /// <summary>One line per row, cells joined by " | ". Rows of empty cells (spacers) are dropped.</summary>
    private static string TableText(string table)
    {
        var rows = Row().Split(ImageAlt().Replace(table, image => $" {image.Groups["alt"].Value} "));
        var lines = rows
            .Select(row => Cell().Split(row).Skip(1).Select(cell => Whitespace().Replace(Tag().Replace(cell, " "), " ").Trim()).ToList())
            .Where(cells => cells.Any(cell => cell.Length > 0))
            .Select(cells => string.Join(CellSeparator, cells));
        return string.Join('\n', lines);
    }

    private static string Clean(string text) => Spaces().Replace(WebUtility.HtmlDecode(Tag().Replace(text, "")), " ").Trim();
}
