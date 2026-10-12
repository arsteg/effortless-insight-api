using System.Text.Json;

namespace EffortlessInsight.Api.Services.EmailSync;

// Sender-filtered time windows avoid mailbox-wide history/delta reads. Page tokens
// remain bound to a fixed window, ending ten minutes ago to allow indexing.
public record EmailScanCursor(DateTime Day, string? Page = null, int Seen = 0, DateTime? Until = null, List<string>? PendingIds = null, string? SenderPolicyHash = null)
{
    public static string PolicyHash(IEnumerable<string> senders) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join("\n", senders.Select(s => s.Trim().TrimStart('@').ToLowerInvariant()).Distinct().OrderBy(s => s, StringComparer.Ordinal)))));

    public static EmailScanCursor ForPolicy(string? value, DateTime initial, string policyHash)
    {
        var cursor = Read(value, initial);
        return cursor.SenderPolicyHash == policyHash ? cursor : new(initial.Date, SenderPolicyHash: policyHash);
    }

    public static EmailScanCursor Read(string? value, DateTime initial)
    {
        try
        {
            if (value?.StartsWith('{') == true)
            {
                var parsed = JsonSerializer.Deserialize<EmailScanCursor>(value);
                if (parsed != null && parsed.Day.Year >= 2000 && parsed.Day <= DateTime.UtcNow) return parsed;
            }
        }
        catch (JsonException) { }
        return new(DateTime.SpecifyKind(initial.Date, DateTimeKind.Utc));
    }
    public string Encode() => JsonSerializer.Serialize(this);
}
