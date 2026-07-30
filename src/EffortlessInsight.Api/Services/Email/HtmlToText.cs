using System.Net;
using System.Text.RegularExpressions;

namespace EffortlessInsight.Api.Services.Email;

/// <summary>
/// Generates a readable plain-text fallback from an HTML email body so every
/// outbound message has a text part (deliverability and accessibility).
/// </summary>
internal static partial class HtmlToText
{
    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = InvisibleBlocksRegex().Replace(html, string.Empty);
        // Keep link targets: <a href="url">label</a> -> "label (url)"
        text = AnchorRegex().Replace(text, static m =>
        {
            var href = m.Groups["href"].Value.Trim();
            var label = TagRegex().Replace(m.Groups["label"].Value, string.Empty).Trim();
            if (label.Length == 0)
            {
                return href;
            }
            return href.Length == 0 || href == "#" || label == href ? label : $"{label} ({href})";
        });
        text = LineBreakTagRegex().Replace(text, "\n");
        text = TagRegex().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(static l => IntraLineWhitespaceRegex().Replace(l, " ").Trim());
        text = string.Join('\n', lines);
        return ExcessBlankLinesRegex().Replace(text, "\n\n").Trim();
    }

    [GeneratedRegex(@"<(head|style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InvisibleBlocksRegex();

    [GeneratedRegex("""<a\b[^>]*href\s*=\s*["'](?<href>[^"']*)["'][^>]*>(?<label>.*?)</a\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|h[1-6]|tr|li|table)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTagRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex IntraLineWhitespaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessBlankLinesRegex();
}
