using EffortlessInsight.Api.Services.EmailSync;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class EmailSenderCheckpointTests
{
    [Fact]
    public void AddingSenderRewindsWithinConfiguredLookback()
    {
        var initial = DateTime.UtcNow.Date.AddDays(-1);
        var old = new EmailScanCursor(DateTime.UtcNow.AddMinutes(-10),
            SenderPolicyHash: EmailScanCursor.PolicyHash(["gst.gov.in"]));
        var hash = EmailScanCursor.PolicyHash(["gst.gov.in", "test@example.com"]);
        var result = EmailScanCursor.ForPolicy(old.Encode(), initial, hash);
        Assert.Equal(initial, result.Day);
        Assert.Equal(hash, result.SenderPolicyHash);
        Assert.Null(result.PendingIds);
    }

    [Fact]
    public void UnchangedPolicyPreservesProgressAndPendingIds()
    {
        var hash = EmailScanCursor.PolicyHash(["@GST.GOV.IN", "test@example.com"]);
        Assert.Equal(hash, EmailScanCursor.PolicyHash(["test@example.com", "gst.gov.in", "gst.gov.in"]));
        var old = new EmailScanCursor(DateTime.UtcNow.AddHours(-1), PendingIds: ["pending"], SenderPolicyHash: hash);
        Assert.Equal(old.Encode(), EmailScanCursor.ForPolicy(old.Encode(), DateTime.UtcNow.AddDays(-1), hash).Encode());
    }

    [Fact]
    public void LegacyCheckpointGetsOneBoundedRescan()
    {
        var initial = DateTime.UtcNow.Date.AddDays(-1);
        var hash = EmailScanCursor.PolicyHash(["gst.gov.in"]);
        var result = EmailScanCursor.ForPolicy(new EmailScanCursor(DateTime.UtcNow.AddHours(-1)).Encode(), initial, hash);
        Assert.Equal(initial, result.Day);
        Assert.Equal(result.Encode(), EmailScanCursor.ForPolicy(result.Encode(), initial.AddDays(-1), hash).Encode());
    }
}
