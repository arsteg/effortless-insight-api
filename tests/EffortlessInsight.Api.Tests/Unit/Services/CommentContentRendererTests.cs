using EffortlessInsight.Api.Services.Collaboration;
using Ganss.Xss;
using Markdig;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class CommentContentRendererTests
{
    private const string UserId = "01a0d6ec-4fd9-71a4-8023-5cd47d719bb8";

    [Fact]
    public void Render_MentionsDisplayNamesWithoutGuidLinks()
    {
        var html = Render($"Please review @[istc test ca]({UserId}) and @[Another user]({UserId}).");
        html.Should().Contain(">@istc test ca</span>").And.Contain(">@Another user</span>");
        html.Should().NotContain($"href=\"{UserId}\"").And.NotContain($"({UserId})").And.NotContain("@[");
    }

    [Fact]
    public void Render_PreservesMarkdownAndEscapesMentionNames()
    {
        var html = Render($"**Review** @[<img src=x onerror=alert(1)>]({UserId})\n\n[Help](https://example.com)");
        html.Should().Contain("<strong>Review</strong>").And.Contain("https://example.com");
        html.Should().NotContain("<img").And.Contain("&lt;img");
    }

    [Fact]
    public void Render_SanitizesHtmlOutsideMentions()
    {
        Render($"<script>alert(1)</script>\n@[CA]({UserId})").Should().NotContain("<script");
    }

    private static string Render(string content)
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("data-user-id");
        return CommentContentRenderer.Render(content, new MarkdownPipelineBuilder().UseAutoLinks().UseEmphasisExtras().Build(), sanitizer);
    }
}
