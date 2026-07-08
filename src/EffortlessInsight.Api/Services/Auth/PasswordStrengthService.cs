using System.Security.Cryptography;
using System.Text;

namespace EffortlessInsight.Api.Services.Auth;

/// <summary>
/// Service for enhanced password validation including common password checks
/// and password history validation.
/// </summary>
public interface IPasswordStrengthService
{
    /// <summary>
    /// Checks if a password is in the common passwords list.
    /// </summary>
    bool IsCommonPassword(string password);

    /// <summary>
    /// Checks if a password contains user-specific information.
    /// </summary>
    bool ContainsUserInfo(string password, string? email, string? name);

    /// <summary>
    /// Checks if a password contains sequential or repeated characters.
    /// </summary>
    bool HasSequentialOrRepeatedChars(string password);

    /// <summary>
    /// Computes a hash for password history comparison (not for storage).
    /// </summary>
    string ComputeHistoryHash(string password);

    /// <summary>
    /// Gets the password strength score (0-100).
    /// </summary>
    int GetStrengthScore(string password);

    /// <summary>
    /// Validates password against all enhanced rules.
    /// Returns list of validation errors.
    /// </summary>
    List<string> ValidateEnhanced(string password, string? email = null, string? name = null);
}

public class PasswordStrengthService : IPasswordStrengthService
{
    // Top 10000 most common passwords (subset for performance)
    // Full list can be loaded from file in production
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Top 100 most common passwords
        "123456", "password", "12345678", "qwerty", "123456789",
        "12345", "1234", "111111", "1234567", "dragon",
        "123123", "baseball", "iloveyou", "trustno1", "sunshine",
        "master", "welcome", "shadow", "ashley", "football",
        "jesus", "michael", "ninja", "mustang", "password1",
        "password123", "letmein", "654321", "superman", "qazwsx",
        "7777777", "121212", "000000", "qwerty123", "123qwe",
        "killer", "zxcvbnm", "aaaaaa", "access", "admin",
        "abc123", "monkey", "1234qwer", "dragon123", "princess",
        "qwertyuiop", "login", "passw0rd", "hello", "charlie",
        "donald", "password1234", "qwerty1", "password12", "admin123",
        "root", "toor", "pass", "test", "guest",
        "master123", "changeme", "p@ssw0rd", "P@ssw0rd", "P@$$w0rd",
        "admin@123", "Admin123", "Admin@123", "welcome1", "Welcome1",
        "welcome123", "Welcome123", "welcome@123", "password!", "Password!",
        "password@123", "Password@123", "password#123", "Password#123",
        "letmein123", "Letmein123", "123456!", "1234567890", "0987654321",
        "qwerty!@#", "asdfghjkl", "zxcvbnm123", "!@#$%^&*", "passpass",
        // India-specific common passwords
        "india123", "india@123", "India123", "India@123",
        "mumbai123", "delhi123", "bangalore123", "chennai123",
        "abcd1234", "1q2w3e4r", "a1b2c3d4", "india2023", "india2024",
        // Business/tax related
        "gst12345", "gstin123", "tax12345", "company123", "business123",
        // Keyboard patterns
        "qwertyui", "asdfghj", "zxcvbn", "!@#$%^&*()", "qweasdzxc",
        "1qaz2wsx", "1qazxsw2", "zaq12wsx", "1q2w3e4r5t", "qazwsxedc",
        // Years and dates
        "2020", "2021", "2022", "2023", "2024", "2025",
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
        // Common words with numbers
        "love123", "baby123", "angel123", "star123", "cool123",
        "tiger123", "lion123", "king123", "queen123", "prince123",
        // Leet speak variations
        "p4ssw0rd", "p@55w0rd", "l3tm3in", "w3lc0m3", "s3cur3",
    };

    // Sequential character patterns to detect
    private static readonly string[] SequentialPatterns =
    {
        "abcdefghijklmnopqrstuvwxyz",
        "zyxwvutsrqponmlkjihgfedcba",
        "01234567890",
        "09876543210",
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
    };

    public bool IsCommonPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        // Check exact match
        if (CommonPasswords.Contains(password))
            return true;

        // Check with common suffixes/prefixes removed
        var normalized = password.ToLowerInvariant();

        // Remove common suffixes like !, @, #, 1, 12, 123, etc.
        var suffixPatterns = new[] { "!", "@", "#", "$", "1", "12", "123", "1234", "!" };
        foreach (var suffix in suffixPatterns)
        {
            if (normalized.EndsWith(suffix) && CommonPasswords.Contains(normalized[..^suffix.Length]))
                return true;
        }

        // Remove common prefixes
        var prefixPatterns = new[] { "my", "the", "a" };
        foreach (var prefix in prefixPatterns)
        {
            if (normalized.StartsWith(prefix) && CommonPasswords.Contains(normalized[prefix.Length..]))
                return true;
        }

        return false;
    }

    public bool ContainsUserInfo(string password, string? email, string? name)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        var lowerPassword = password.ToLowerInvariant();

        // Check email parts
        if (!string.IsNullOrEmpty(email))
        {
            var emailParts = email.ToLowerInvariant().Split('@', '.', '_', '-', '+');
            foreach (var part in emailParts.Where(p => p.Length >= 3))
            {
                if (lowerPassword.Contains(part))
                    return true;
            }
        }

        // Check name parts
        if (!string.IsNullOrEmpty(name))
        {
            var nameParts = name.ToLowerInvariant().Split(' ', '-', '\'', '.');
            foreach (var part in nameParts.Where(p => p.Length >= 3))
            {
                if (lowerPassword.Contains(part))
                    return true;
            }
        }

        return false;
    }

    public bool HasSequentialOrRepeatedChars(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 4)
            return false;

        var lowerPassword = password.ToLowerInvariant();

        // Check for sequential characters (4+ in a row)
        foreach (var pattern in SequentialPatterns)
        {
            for (int i = 0; i <= pattern.Length - 4; i++)
            {
                if (lowerPassword.Contains(pattern.Substring(i, 4)))
                    return true;
            }
        }

        // Check for repeated characters (4+ same char)
        for (int i = 0; i <= password.Length - 4; i++)
        {
            if (password[i] == password[i + 1] &&
                password[i] == password[i + 2] &&
                password[i] == password[i + 3])
                return true;
        }

        return false;
    }

    public string ComputeHistoryHash(string password)
    {
        // Use SHA-256 for password history comparison
        // This is NOT for password storage (use Argon2 for that)
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hash);
    }

    public int GetStrengthScore(string password)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        int score = 0;

        // Length scoring (up to 30 points)
        score += Math.Min(password.Length * 3, 30);

        // Character diversity (up to 40 points)
        if (password.Any(char.IsLower)) score += 10;
        if (password.Any(char.IsUpper)) score += 10;
        if (password.Any(char.IsDigit)) score += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score += 10;

        // Penalty for common patterns (up to -30 points)
        if (IsCommonPassword(password)) score -= 30;
        if (HasSequentialOrRepeatedChars(password)) score -= 15;

        // Bonus for mixed character positions (up to 10 points)
        var hasInterleavedTypes = false;
        for (int i = 1; i < password.Length; i++)
        {
            if (char.IsLetter(password[i]) != char.IsLetter(password[i - 1]) ||
                char.IsUpper(password[i]) != char.IsUpper(password[i - 1]))
            {
                hasInterleavedTypes = true;
                break;
            }
        }
        if (hasInterleavedTypes) score += 10;

        // Unique character ratio bonus (up to 10 points)
        var uniqueRatio = (double)password.Distinct().Count() / password.Length;
        score += (int)(uniqueRatio * 10);

        return Math.Clamp(score, 0, 100);
    }

    public List<string> ValidateEnhanced(string password, string? email = null, string? name = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required");
            return errors;
        }

        if (IsCommonPassword(password))
        {
            errors.Add("This password is too common. Please choose a more unique password.");
        }

        if (ContainsUserInfo(password, email, name))
        {
            errors.Add("Password should not contain your name or email address.");
        }

        if (HasSequentialOrRepeatedChars(password))
        {
            errors.Add("Password should not contain sequential or repeated characters (e.g., 'abcd', '1111').");
        }

        var strengthScore = GetStrengthScore(password);
        if (strengthScore < 40)
        {
            errors.Add($"Password strength is too weak (score: {strengthScore}/100). Use a mix of characters.");
        }

        return errors;
    }
}
