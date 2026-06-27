using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace EffortlessInsight.Api.Services.Auth;

/// <summary>
/// Custom password hasher using BCrypt algorithm.
/// BCrypt is memory-hard and resistant to GPU/ASIC attacks.
/// Uses BCrypt.Net-Next library with work factor 12 (OWASP recommended).
/// </summary>
/// <remarks>
/// BCrypt is chosen over Argon2id because:
/// - .NET does not have built-in Argon2id support
/// - BCrypt.Net-Next is already installed in the project
/// - BCrypt with work factor 12+ meets OWASP recommendations
/// - BCrypt has been battle-tested for over 25 years
/// </remarks>
public class SecurePasswordHasher<TUser> : IPasswordHasher<TUser> where TUser : class
{
    // BCrypt work factor (OWASP recommends minimum 10, we use 12 for extra security)
    // Each increment doubles the computation time
    private const int WorkFactor = 12;

    // Prefix to identify our BCrypt hashes vs legacy Identity hashes
    private const string BcryptPrefix = "$2";

    private readonly ILogger<SecurePasswordHasher<TUser>> _logger;

    public SecurePasswordHasher(ILogger<SecurePasswordHasher<TUser>> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Hash a password using BCrypt.
    /// </summary>
    public string HashPassword(TUser user, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentNullException(nameof(password));
        }

        // BCrypt automatically generates a secure random salt and embeds it in the hash
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, WorkFactor);
    }

    /// <summary>
    /// Verify a password against a stored hash.
    /// </summary>
    public PasswordVerificationResult VerifyHashedPassword(TUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrEmpty(hashedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        if (string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        // Check if this is a BCrypt hash
        if (hashedPassword.StartsWith(BcryptPrefix))
        {
            try
            {
                if (BCrypt.Net.BCrypt.EnhancedVerify(providedPassword, hashedPassword))
                {
                    // Check if work factor needs to be increased
                    var currentWorkFactor = GetWorkFactorFromHash(hashedPassword);
                    if (currentWorkFactor < WorkFactor)
                    {
                        _logger.LogInformation("Password verified but uses lower work factor ({Current} vs {Target}). Rehash recommended.",
                            currentWorkFactor, WorkFactor);
                        return PasswordVerificationResult.SuccessRehashNeeded;
                    }
                    return PasswordVerificationResult.Success;
                }
                return PasswordVerificationResult.Failed;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BCrypt verification failed");
                return PasswordVerificationResult.Failed;
            }
        }

        // Try legacy ASP.NET Identity V3 hash format
        return VerifyLegacyHash(hashedPassword, providedPassword);
    }

    /// <summary>
    /// Extract work factor from BCrypt hash.
    /// BCrypt hash format: $2a$12$... where 12 is the work factor.
    /// </summary>
    private static int GetWorkFactorFromHash(string hash)
    {
        try
        {
            // Format: $2a$XX$... or $2b$XX$... or $2y$XX$...
            var parts = hash.Split('$');
            if (parts.Length >= 3 && int.TryParse(parts[2], out var workFactor))
            {
                return workFactor;
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        return WorkFactor; // Assume current work factor if parsing fails
    }

    /// <summary>
    /// Attempt to verify using legacy ASP.NET Identity V3 hash format.
    /// This allows seamless migration from old hashes.
    /// </summary>
    private PasswordVerificationResult VerifyLegacyHash(string hashedPassword, string providedPassword)
    {
        try
        {
            // Try to decode as base64 (Identity V3 format)
            byte[] decodedHash;
            try
            {
                decodedHash = Convert.FromBase64String(hashedPassword);
            }
            catch (FormatException)
            {
                return PasswordVerificationResult.Failed;
            }

            // ASP.NET Identity V3 format: 0x01 | prf | iter | saltlen | salt | subkey
            if (decodedHash.Length == 0 || decodedHash[0] != 0x01)
            {
                return PasswordVerificationResult.Failed;
            }

            // Identity V3 format uses PBKDF2
            // Format: [0x01][prf=1 byte][iter count=4 bytes][salt length=4 bytes][salt][hash]
            if (decodedHash.Length < 13)
            {
                return PasswordVerificationResult.Failed;
            }

            var prf = (KeyDerivationPrf)ReadNetworkByteOrder(decodedHash, 1);
            var iterCount = (int)ReadNetworkByteOrder(decodedHash, 5);
            var saltLength = (int)ReadNetworkByteOrder(decodedHash, 9);

            if (saltLength < 0 || decodedHash.Length < 13 + saltLength)
            {
                return PasswordVerificationResult.Failed;
            }

            var salt = new byte[saltLength];
            Buffer.BlockCopy(decodedHash, 13, salt, 0, saltLength);

            var subkeyLength = decodedHash.Length - 13 - saltLength;
            if (subkeyLength < 0)
            {
                return PasswordVerificationResult.Failed;
            }

            var expectedSubkey = new byte[subkeyLength];
            Buffer.BlockCopy(decodedHash, 13 + saltLength, expectedSubkey, 0, subkeyLength);

            // Derive key using PBKDF2
            var actualSubkey = DeriveKey(providedPassword, salt, prf, iterCount, subkeyLength);

            if (CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey))
            {
                // Password is correct, but hash is legacy format
                // Suggest rehashing with BCrypt
                _logger.LogInformation("Legacy PBKDF2 password hash verified. Will rehash with BCrypt on next login.");
                return PasswordVerificationResult.SuccessRehashNeeded;
            }

            return PasswordVerificationResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify legacy password hash");
            return PasswordVerificationResult.Failed;
        }
    }

    private static uint ReadNetworkByteOrder(byte[] buffer, int offset)
    {
        return ((uint)buffer[offset] << 24)
            | ((uint)buffer[offset + 1] << 16)
            | ((uint)buffer[offset + 2] << 8)
            | buffer[offset + 3];
    }

    private static byte[] DeriveKey(string password, byte[] salt, KeyDerivationPrf prf, int iterationCount, int numBytesRequested)
    {
        var hashAlgorithmName = prf switch
        {
            KeyDerivationPrf.HMACSHA1 => HashAlgorithmName.SHA1,
            KeyDerivationPrf.HMACSHA256 => HashAlgorithmName.SHA256,
            KeyDerivationPrf.HMACSHA512 => HashAlgorithmName.SHA512,
            _ => throw new ArgumentOutOfRangeException(nameof(prf))
        };

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterationCount,
            hashAlgorithmName,
            numBytesRequested);
    }

    private enum KeyDerivationPrf
    {
        HMACSHA1 = 0,
        HMACSHA256 = 1,
        HMACSHA512 = 2,
    }
}
