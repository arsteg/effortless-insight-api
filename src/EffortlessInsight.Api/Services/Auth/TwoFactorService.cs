using System.Security.Cryptography;
using System.Text;
using OtpNet;
using QRCoder;

namespace EffortlessInsight.Api.Services.Auth;

public interface ITwoFactorService
{
    (string Secret, string QrCodeDataUrl, string OtpauthUrl, List<string> BackupCodes) GenerateSetup(string email);
    bool VerifyCode(string secret, string code);
    bool VerifyBackupCode(string[] hashedCodes, string code, out int usedIndex);
    string[] HashBackupCodes(List<string> codes);
    byte[] EncryptSecret(string secret);
    string DecryptSecret(byte[] encryptedSecret);
}

public class TwoFactorService : ITwoFactorService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<TwoFactorService> _logger;
    private readonly string _issuer;
    private readonly byte[] _encryptionKey;

    public TwoFactorService(IConfiguration configuration, ILogger<TwoFactorService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _issuer = _configuration["TwoFactor:Issuer"] ?? "EffortlessInsight";

        var keyString = _configuration["TwoFactor:EncryptionKey"]
            ?? throw new InvalidOperationException("TwoFactor:EncryptionKey not configured");
        _encryptionKey = Encoding.UTF8.GetBytes(keyString.PadRight(32).Substring(0, 32));
    }

    public (string Secret, string QrCodeDataUrl, string OtpauthUrl, List<string> BackupCodes) GenerateSetup(string email)
    {
        // Generate a random secret key (20 bytes = 160 bits, standard for TOTP)
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secret = Base32Encoding.ToString(secretBytes);

        // Generate the otpauth URL for authenticator apps
        var otpauthUrl = $"otpauth://totp/{_issuer}:{email}?secret={secret}&issuer={_issuer}&algorithm=SHA1&digits=6&period=30";

        // Generate QR code as Base64 data URL
        var qrCodeDataUrl = GenerateQrCodeDataUrl(otpauthUrl);

        // Generate backup codes
        var backupCodes = GenerateBackupCodes(8);

        return (secret, qrCodeDataUrl, otpauthUrl, backupCodes);
    }

    public bool VerifyCode(string secret, string code)
    {
        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);

            // Verify with a window of 1 (allows for 30 seconds clock drift)
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to verify TOTP code");
            return false;
        }
    }

    public bool VerifyBackupCode(string[] hashedCodes, string code, out int usedIndex)
    {
        usedIndex = -1;
        if (hashedCodes == null || hashedCodes.Length == 0)
            return false;

        var normalizedCode = code.ToUpperInvariant().Replace("-", "").Replace(" ", "");
        var codeHash = ComputeSha256Hash(normalizedCode);

        for (int i = 0; i < hashedCodes.Length; i++)
        {
            if (!string.IsNullOrEmpty(hashedCodes[i]) && hashedCodes[i] == codeHash)
            {
                usedIndex = i;
                return true;
            }
        }

        return false;
    }

    public string[] HashBackupCodes(List<string> codes)
    {
        return codes.Select(c => ComputeSha256Hash(c.ToUpperInvariant())).ToArray();
    }

    public byte[] EncryptSecret(string secret)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var encryptedBytes = encryptor.TransformFinalBlock(secretBytes, 0, secretBytes.Length);

        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return result;
    }

    public string DecryptSecret(byte[] encryptedSecret)
    {
        if (encryptedSecret == null || encryptedSecret.Length < 17) // IV (16) + at least 1 byte
            throw new ArgumentException("Invalid encrypted secret");

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        // Extract IV from first 16 bytes
        var iv = new byte[16];
        Buffer.BlockCopy(encryptedSecret, 0, iv, 0, 16);
        aes.IV = iv;

        // Extract encrypted data
        var encryptedData = new byte[encryptedSecret.Length - 16];
        Buffer.BlockCopy(encryptedSecret, 16, encryptedData, 0, encryptedData.Length);

        using var decryptor = aes.CreateDecryptor();
        var decryptedBytes = decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private static string GenerateQrCodeDataUrl(string content)
    {
        using var qrGenerator = new QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = qrCode.GetGraphic(5);
        return $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
    }

    private static List<string> GenerateBackupCodes(int count)
    {
        var codes = new List<string>();
        var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Excluded I, O, 0, 1 to avoid confusion

        for (int i = 0; i < count; i++)
        {
            var code = new char[8];
            var randomBytes = RandomNumberGenerator.GetBytes(8);

            for (int j = 0; j < 8; j++)
            {
                code[j] = chars[randomBytes[j] % chars.Length];
            }

            codes.Add(new string(code));
        }

        return codes;
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower();
    }
}
