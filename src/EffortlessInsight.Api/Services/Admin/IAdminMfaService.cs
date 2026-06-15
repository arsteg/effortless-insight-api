namespace EffortlessInsight.Api.Services.Admin;

/// <summary>
/// Service for admin MFA (Multi-Factor Authentication) operations.
/// </summary>
public interface IAdminMfaService
{
    /// <summary>
    /// Generate a new TOTP secret for MFA setup.
    /// </summary>
    /// <param name="adminEmail">Admin email for QR code label.</param>
    /// <returns>Base32 secret, QR code URI, and backup codes.</returns>
    (string Secret, string QrCodeUri, List<string> BackupCodes) GenerateSetupData(string adminEmail);

    /// <summary>
    /// Verify a TOTP code against the secret.
    /// </summary>
    /// <param name="secret">Base32 TOTP secret.</param>
    /// <param name="code">6-digit code to verify.</param>
    /// <returns>True if code is valid.</returns>
    bool VerifyCode(string secret, string code);

    /// <summary>
    /// Verify a TOTP code against encrypted secret.
    /// </summary>
    /// <param name="encryptedSecret">AES-encrypted TOTP secret.</param>
    /// <param name="code">6-digit code to verify.</param>
    /// <returns>True if code is valid.</returns>
    bool VerifyCodeWithEncryptedSecret(byte[] encryptedSecret, string code);

    /// <summary>
    /// Encrypt the MFA secret for storage.
    /// </summary>
    /// <param name="secret">Plain text secret.</param>
    /// <returns>Encrypted secret bytes.</returns>
    byte[] EncryptSecret(string secret);

    /// <summary>
    /// Decrypt the MFA secret.
    /// </summary>
    /// <param name="encryptedSecret">Encrypted secret bytes.</param>
    /// <returns>Plain text secret.</returns>
    string DecryptSecret(byte[] encryptedSecret);

    /// <summary>
    /// Hash backup codes for storage.
    /// </summary>
    /// <param name="backupCodes">List of plain backup codes.</param>
    /// <returns>List of hashed backup codes.</returns>
    List<string> HashBackupCodes(List<string> backupCodes);

    /// <summary>
    /// Verify a backup code against hashed codes.
    /// </summary>
    /// <param name="code">Plain backup code.</param>
    /// <param name="hashedCodes">List of hashed codes.</param>
    /// <returns>Index of matching code, or -1 if not found.</returns>
    int VerifyBackupCode(string code, List<string> hashedCodes);
}
