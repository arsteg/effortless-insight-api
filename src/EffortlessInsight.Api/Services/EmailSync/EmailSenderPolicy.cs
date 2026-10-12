using System.Net.Mail;
using System.Text.RegularExpressions;

namespace EffortlessInsight.Api.Services.EmailSync;

public static class EmailSenderPolicy
{
    public static bool IsValidRule(string rule) => Regex.IsMatch(rule,
        @"^(?:[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@|@?)[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?\.[A-Za-z]{2,}$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool IsAllowed(string sender, IEnumerable<string> rules)
    {
        if (!MailAddress.TryCreate(sender, out var address)) return false;
        return rules.Where(IsValidRule).Any(rule => rule.StartsWith('@') || !rule.Contains('@')
            ? address.Host.Equals(rule.TrimStart('@'), StringComparison.OrdinalIgnoreCase)
            : address.Address.Equals(rule, StringComparison.OrdinalIgnoreCase));
    }
}
