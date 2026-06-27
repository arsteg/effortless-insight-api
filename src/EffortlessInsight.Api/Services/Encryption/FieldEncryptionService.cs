using System.Security.Cryptography;
using System.Text;

namespace EffortlessInsight.Api.Services.Encryption;

/// <summary>
/// Service for encrypting and decrypting sensitive PII fields using AES-256-GCM.
/// Implements DPDP Act compliance requirements for encryption at rest.
/// </summary>
public interface IFieldEncryptionService
{
    /// <summary>
    /// Encrypts a plaintext value.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts an encrypted value.
    /// </summary>
    string Decrypt(string ciphertext);

    /// <summary>
    /// Checks if a value is encrypted.
    /// </summary>
    bool IsEncrypted(string value);
}

/// <summary>
/// Static accessor for the field encryption service.
/// Used by EF Core value converters which cannot use dependency injection.
/// </summary>
public static class FieldEncryptionServiceAccessor
{
    private static IFieldEncryptionService? _instance;

    /// <summary>
    /// Gets or sets the current encryption service instance.
    /// Must be set during application startup.
    /// </summary>
    public static IFieldEncryptionService Instance
    {
        get => _instance ?? throw new InvalidOperationException(
            "FieldEncryptionServiceAccessor.Instance not configured. Call SetInstance() during startup.");
        set => _instance = value;
    }

    /// <summary>
    /// Sets the encryption service instance. Call this during application startup.
    /// </summary>
    public static void SetInstance(IFieldEncryptionService service)
    {
        _instance = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>
    /// Checks if the instance has been configured.
    /// </summary>
    public static bool IsConfigured => _instance != null;
}

public class FieldEncryptionService : IFieldEncryptionService
{
    private readonly byte[] _key;
    private readonly ILogger<FieldEncryptionService> _logger;

    // Prefix to identify encrypted values
    private const string EncryptedPrefix = "ENC:";
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16;   // 128 bits authentication tag

    public FieldEncryptionService(IConfiguration configuration, ILogger<FieldEncryptionService> logger)
    {
        _logger = logger;

        var keyString = configuration["Encryption:FieldEncryptionKey"];
        if (string.IsNullOrEmpty(keyString))
        {
            _logger.LogWarning("Encryption:FieldEncryptionKey not configured. Using development key. Configure a secure key for production!");
            // Development fallback - generate deterministic key from a seed
            // In production, this MUST be a proper 256-bit key from secrets manager
            keyString = "DevKey-DO-NOT-USE-IN-PRODUCTION-32";
        }

        // Derive a 256-bit key from the configured key using SHA-256
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        // Don't double-encrypt
        if (IsEncrypted(plaintext))
            return plaintext;

        try
        {
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

            // Generate random nonce
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            // Prepare output buffer
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];

            // Encrypt using AES-256-GCM
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            // Combine nonce + ciphertext + tag
            var result = new byte[NonceSize + ciphertext.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

            return EncryptedPrefix + Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt field value");
            throw new InvalidOperationException("Encryption failed", ex);
        }
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return ciphertext;

        // Only decrypt if it's actually encrypted
        if (!IsEncrypted(ciphertext))
            return ciphertext;

        try
        {
            // Remove prefix and decode
            var encryptedData = Convert.FromBase64String(ciphertext[EncryptedPrefix.Length..]);

            if (encryptedData.Length < NonceSize + TagSize)
            {
                throw new InvalidOperationException("Invalid encrypted data format");
            }

            // Extract nonce, ciphertext, and tag
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var encryptedBytes = new byte[encryptedData.Length - NonceSize - TagSize];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(encryptedData, NonceSize, encryptedBytes, 0, encryptedBytes.Length);
            Buffer.BlockCopy(encryptedData, NonceSize + encryptedBytes.Length, tag, 0, TagSize);

            // Decrypt
            var plaintextBytes = new byte[encryptedBytes.Length];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, encryptedBytes, tag, plaintextBytes);

            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt field value");
            throw new InvalidOperationException("Decryption failed", ex);
        }
    }

    public bool IsEncrypted(string value)
    {
        return !string.IsNullOrEmpty(value) && value.StartsWith(EncryptedPrefix);
    }
}

/// <summary>
/// EF Core value converter for automatic field encryption/decryption.
/// Uses the static FieldEncryptionServiceAccessor for the encryption service.
/// </summary>
public class EncryptedStringConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string?, string?>
{
    /// <summary>
    /// Creates a new instance using the static service accessor.
    /// </summary>
    public EncryptedStringConverter()
        : base(
            v => v == null ? null : FieldEncryptionServiceAccessor.Instance.Encrypt(v),
            v => v == null ? null : FieldEncryptionServiceAccessor.Instance.Decrypt(v))
    {
    }

    /// <summary>
    /// Creates a new instance with a specific encryption service.
    /// </summary>
    public EncryptedStringConverter(IFieldEncryptionService encryptionService)
        : base(
            v => v == null ? null : encryptionService.Encrypt(v),
            v => v == null ? null : encryptionService.Decrypt(v))
    {
    }
}
