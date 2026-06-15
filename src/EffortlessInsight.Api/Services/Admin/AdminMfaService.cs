using System.Security.Cryptography;
using System.Text;
using EffortlessInsight.Api.Options;
using Microsoft.Extensions.Options;
using OtpNet;

namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for admin MFA (Multi-Factor Authentication) operations using TOTP.
/// </summary>
public class AdminMfaService : IAdminMfaService
{
    private readonly AdminAuthOptions _options;
    private readonly ILogger<AdminMfaService> _logger;
    private readonly byte[] _encryptionKey;

    public AdminMfaService(
        IOptions<AdminAuthOptions> options,
        ILogger<AdminMfaService> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Derive encryption key from configured key
        if (!string.IsNullOrEmpty(_options.MfaEncryptionKey))
        {
            _encryptionKey = Convert.FromBase64String(_options.MfaEncryptionKey);
        }
        else
        {
            // Development fallback - should not be used in production
            _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes("dev-mfa-key-not-for-production"));
            _logger.LogWarning("Using development MFA encryption key. Configure AdminAuth:MfaEncryptionKey in production.");
        }
    }

    public (string Secret, string QrCodeUri, List<string> BackupCodes) GenerateSetupData(string adminEmail)
    {
        // Generate a random 20-byte secret
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(secretBytes);

        // Generate QR code URI (otpauth:// format)
        var qrCodeUri = new OtpUri(
            OtpType.Totp,
            secretBytes,
            adminEmail,
            _options.MfaIssuer).ToString();

        // Generate 10 backup codes
        var backupCodes = GenerateBackupCodes(10);

        return (secret, qrCodeUri, backupCodes);
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
        {
            return false;
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

            // Allow for time drift (1 step before and after)
            return totp.VerifyTotp(code, out _, new VerificationWindow(1, 1));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "TOTP verification failed");
            return false;
        }
    }

    public bool VerifyCodeWithEncryptedSecret(byte[] encryptedSecret, string code)
    {
        try
        {
            var secret = DecryptSecret(encryptedSecret);
            return VerifyCode(secret, code);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to verify TOTP with encrypted secret");
            return false;
        }
    }

    public byte[] EncryptSecret(string secret)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var encrypted = encryptor.TransformFinalBlock(secretBytes, 0, secretBytes.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        aes.IV.CopyTo(result, 0);
        encrypted.CopyTo(result, aes.IV.Length);

        return result;
    }

    public string DecryptSecret(byte[] encryptedSecret)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        // Extract IV from encrypted data
        var iv = new byte[16];
        var encrypted = new byte[encryptedSecret.Length - 16];
        Array.Copy(encryptedSecret, 0, iv, 0, 16);
        Array.Copy(encryptedSecret, 16, encrypted, 0, encrypted.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);

        return Encoding.UTF8.GetString(decrypted);
    }

    public List<string> HashBackupCodes(List<string> backupCodes)
    {
        return backupCodes.Select(code => BCrypt.Net.BCrypt.HashPassword(code, 10)).ToList();
    }

    public int VerifyBackupCode(string code, List<string> hashedCodes)
    {
        for (int i = 0; i < hashedCodes.Count; i++)
        {
            if (BCrypt.Net.BCrypt.Verify(code, hashedCodes[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static List<string> GenerateBackupCodes(int count)
    {
        var codes = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            // Generate 8-character alphanumeric code in format XXXX-XXXX
            var bytes = RandomNumberGenerator.GetBytes(5);
            var code = Convert.ToHexString(bytes).ToUpperInvariant();
            codes.Add($"{code[..4]}-{code[4..8]}");
        }
        return codes;
    }
}
