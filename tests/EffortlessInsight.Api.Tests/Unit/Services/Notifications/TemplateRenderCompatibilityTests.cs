using EffortlessInsight.Api.Services.Notifications;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace EffortlessInsight.Api.Tests.Unit.Services.Notifications;

public class TemplateRenderCompatibilityTests
{
    [Fact]
    public async Task NoticeTemplateRendersConditionalsFiltersAndCurrencyHelpers()
    {
        using var db = BillingTestDbContextFactory.Create();
        var service = new TemplateRenderService(db, NullLogger<TemplateRenderService>.Instance);
        var result = await service.RenderAsync("{{ if notice_number }}{{ notice_number | string.upcase }}: {{ format_currency amount }}{{ end }}",
            new Dictionary<string, object> { ["notice_number"] = "drc-01", ["amount"] = 1234.50m });
        Assert.Equal("DRC-01: 1,234.50", result);
    }

    [Fact]
    public async Task CachedTemplateDoesNotRetainAnotherRecipientsData()
    {
        using var db = BillingTestDbContextFactory.Create();
        var service = new TemplateRenderService(db, NullLogger<TemplateRenderService>.Instance);
        const string template = "{{ user.name }}: {{ for item in notices }}{{ item }} {{ end }}";
        var first = await service.RenderAsync(template, new Dictionary<string, object> {
            ["user"] = new { name = "First" }, ["notices"] = new[] { "DRC-01" } });
        var second = await service.RenderAsync(template, new Dictionary<string, object> {
            ["user"] = new { name = "Second" }, ["notices"] = new[] { "ASMT-10" } });
        Assert.Equal("First: DRC-01 ", first);
        Assert.Equal("Second: ASMT-10 ", second);
    }
}
