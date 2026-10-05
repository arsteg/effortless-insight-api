using System.Net;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Markdig;

namespace EffortlessInsight.Api.Services.Collaboration;

public static partial class CommentContentRenderer
{
    // Render mention labels independently of membership/notification eligibility.
    // Never feed the mention's storage syntax to Markdown's link parser.
    public static string Render(string content, MarkdownPipeline pipeline, HtmlSanitizer sanitizer)
    {
        var formatted = MentionPattern().Replace(content, match =>
            $"<span class=\"mention\" data-user-id=\"{match.Groups[2].Value}\">@{WebUtility.HtmlEncode(match.Groups[1].Value)}</span>");
        return sanitizer.Sanitize(Markdown.ToHtml(formatted, pipeline));
    }

    [GeneratedRegex(@"@\[([^\]]+)\]\(([a-fA-F0-9\-]{36})\)")]
    private static partial Regex MentionPattern();
}
