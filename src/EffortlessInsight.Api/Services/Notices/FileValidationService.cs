using System.Security.Cryptography;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Result of file validation.
/// </summary>
public record FileValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DetectedMimeType { get; init; }
    public string? SanitizedFileName { get; init; }
    public long FileSize { get; init; }
    public string? FileHash { get; init; }

    public static FileValidationResult Success(
        string detectedMimeType,
        string sanitizedFileName,
        long fileSize,
        string fileHash) => new()
    {
        IsValid = true,
        DetectedMimeType = detectedMimeType,
        SanitizedFileName = sanitizedFileName,
        FileSize = fileSize,
        FileHash = fileHash
    };

    public static FileValidationResult Failure(string errorCode, string errorMessage) => new()
    {
        IsValid = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Result of duplicate check.
/// </summary>
public record DuplicateCheckResult
{
    public bool IsPotentialDuplicate { get; init; }
    public Guid? ExistingNoticeId { get; init; }
    public string? ExistingNoticeNumber { get; init; }
    public DateTime? UploadedAt { get; init; }
    public decimal SimilarityScore { get; init; }

    public static DuplicateCheckResult NoDuplicate() => new()
    {
        IsPotentialDuplicate = false,
        SimilarityScore = 0
    };

    public static DuplicateCheckResult Duplicate(
        Guid existingNoticeId,
        string? noticeNumber,
        DateTime uploadedAt,
        decimal similarityScore = 1.0m) => new()
    {
        IsPotentialDuplicate = true,
        ExistingNoticeId = existingNoticeId,
        ExistingNoticeNumber = noticeNumber,
        UploadedAt = uploadedAt,
        SimilarityScore = similarityScore
    };
}

/// <summary>
/// Service for validating notice files before upload.
/// </summary>
public interface IFileValidationService
{
    /// <summary>
    /// Validates a file for upload.
    /// </summary>
    Task<FileValidationResult> ValidateAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates file metadata without reading content.
    /// </summary>
    FileValidationResult ValidateMetadata(
        string fileName,
        string? contentType,
        long fileSize);

    /// <summary>
    /// Calculates SHA-256 hash of file content.
    /// </summary>
    Task<string> CalculateHashAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sanitizes a filename for safe storage.
    /// </summary>
    string SanitizeFileName(string fileName);
}

/// <summary>
/// Implementation of file validation service.
/// </summary>
public class FileValidationService : IFileValidationService
{
    private readonly ILogger<FileValidationService> _logger;

    // Maximum file size: 25 MB
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    // Allowed file extensions and their MIME types
    private static readonly Dictionary<string, string[]> AllowedFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".heic"] = ["image/heic", "image/heif"],
        [".heif"] = ["image/heic", "image/heif"]
    };

    // Magic bytes for file type detection
    private static readonly Dictionary<string, byte[][]> MagicBytes = new()
    {
        ["application/pdf"] = ["%PDF"u8.ToArray()],
        ["image/jpeg"] = [[0xFF, 0xD8, 0xFF]],
        ["image/png"] = [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]],
        ["image/heic"] = [
            // ftyp box with heic brand
            [0x00, 0x00, 0x00], // Variable length
        ]
    };

    public FileValidationService(ILogger<FileValidationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FileValidationResult> ValidateAsync(
        Stream fileStream,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        // Validate metadata first
        var metadataResult = ValidateMetadata(fileName, contentType, fileStream.Length);
        if (!metadataResult.IsValid)
        {
            return metadataResult;
        }

        // Ensure stream is at the beginning
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        // Read header for magic bytes check
        var headerBuffer = new byte[16];
        var bytesRead = await fileStream.ReadAsync(headerBuffer, cancellationToken);

        if (bytesRead < 4)
        {
            return FileValidationResult.Failure(
                "FILE_TOO_SMALL",
                "File is too small to be a valid document");
        }

        // Detect actual file type from magic bytes
        var detectedMimeType = DetectMimeType(headerBuffer, fileName);
        if (detectedMimeType == null)
        {
            return FileValidationResult.Failure(
                "INVALID_FILE_CONTENT",
                "Unable to process file. File may be corrupted or not a valid PDF, JPG, PNG, or HEIC file.");
        }

        // Verify the detected type matches the claimed type
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedFileTypes.TryGetValue(extension, out var allowedMimes) ||
            !allowedMimes.Contains(detectedMimeType, StringComparer.OrdinalIgnoreCase))
        {
            return FileValidationResult.Failure(
                "FILE_TYPE_MISMATCH",
                $"Unable to process file. The file content does not match the '{extension}' extension. File may be corrupted.");
        }

        // Check for password-protected PDF
        if (detectedMimeType == "application/pdf")
        {
            var isPasswordProtected = await CheckPdfPasswordProtectionAsync(fileStream, cancellationToken);
            if (isPasswordProtected)
            {
                return FileValidationResult.Failure(
                    "PDF_PASSWORD_PROTECTED",
                    "Password-protected files are not supported. Please remove the password protection and try again.");
            }
        }

        // Reset stream for hash calculation
        if (fileStream.CanSeek)
        {
            fileStream.Position = 0;
        }

        // Calculate file hash
        var fileHash = await CalculateHashAsync(fileStream, cancellationToken);

        // Sanitize filename
        var sanitizedFileName = SanitizeFileName(fileName);

        _logger.LogInformation(
            "File validation successful: {FileName} ({MimeType}, {Size} bytes, hash: {Hash})",
            sanitizedFileName, detectedMimeType, fileStream.Length, fileHash[..16]);

        return FileValidationResult.Success(
            detectedMimeType,
            sanitizedFileName,
            fileStream.Length,
            fileHash);
    }

    /// <inheritdoc />
    public FileValidationResult ValidateMetadata(
        string fileName,
        string? contentType,
        long fileSize)
    {
        // Check file size
        if (fileSize <= 0)
        {
            return FileValidationResult.Failure(
                "FILE_EMPTY",
                "File is empty");
        }

        if (fileSize > MaxFileSizeBytes)
        {
            return FileValidationResult.Failure(
                "FILE_TOO_LARGE",
                $"File exceeds maximum size of {MaxFileSizeBytes / (1024 * 1024)}MB");
        }

        // Check filename
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Failure(
                "INVALID_FILENAME",
                "File name is required");
        }

        // Check extension
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return FileValidationResult.Failure(
                "INVALID_FILE_TYPE",
                $"File must have an extension. Allowed: PDF, JPG, PNG, HEIC");
        }

        if (!AllowedFileTypes.ContainsKey(extension))
        {
            return FileValidationResult.Failure(
                "INVALID_FILE_TYPE",
                $"File type '{extension}' not supported. Allowed: PDF, JPG, PNG, HEIC");
        }

        // Check content type if provided
        if (!string.IsNullOrEmpty(contentType))
        {
            var isValidContentType = AllowedFileTypes.Values
                .Any(mimes => mimes.Contains(contentType, StringComparer.OrdinalIgnoreCase));

            if (!isValidContentType)
            {
                return FileValidationResult.Failure(
                    "INVALID_CONTENT_TYPE",
                    $"Content type '{contentType}' not supported");
            }
        }

        return FileValidationResult.Success(
            contentType ?? "application/octet-stream",
            SanitizeFileName(fileName),
            fileSize,
            string.Empty);
    }

    /// <inheritdoc />
    public async Task<string> CalculateHashAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        // Remove path components
        fileName = Path.GetFileName(fileName);

        // Replace dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|', ';', '&', '$', '`', '!', '@', '#', '%', '^', '(', ')', '[', ']', '{', '}'])
            .ToHashSet();

        var sanitized = new char[fileName.Length];
        for (var i = 0; i < fileName.Length; i++)
        {
            sanitized[i] = invalidChars.Contains(fileName[i]) ? '_' : fileName[i];
        }

        var result = new string(sanitized).Trim('_', ' ', '.');

        // Limit length while preserving extension
        if (result.Length > 200)
        {
            var ext = Path.GetExtension(result);
            var name = Path.GetFileNameWithoutExtension(result);
            result = name[..(200 - ext.Length)] + ext;
        }

        return string.IsNullOrEmpty(result) ? "unnamed" : result;
    }

    /// <summary>
    /// Checks if a PDF file is password-protected by looking for /Encrypt dictionary.
    /// </summary>
    private async Task<bool> CheckPdfPasswordProtectionAsync(
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        try
        {
            if (fileStream.CanSeek)
            {
                fileStream.Position = 0;
            }

            // Read the PDF content to search for encryption markers
            // We'll read in chunks to handle large files efficiently
            // Password-protected PDFs have /Encrypt in their trailer or xref
            const int bufferSize = 8192;
            const int maxBytesToRead = 1024 * 1024; // Read up to 1MB
            var buffer = new byte[bufferSize];
            var totalBytesRead = 0;

            // We're looking for "/Encrypt" which indicates encryption
            var encryptMarker = "/Encrypt"u8.ToArray();

            while (totalBytesRead < maxBytesToRead)
            {
                var bytesRead = await fileStream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    break;

                // Search for /Encrypt in the buffer
                if (ContainsSequence(buffer, bytesRead, encryptMarker))
                {
                    _logger.LogInformation("Detected password-protected PDF");
                    return true;
                }

                totalBytesRead += bytesRead;

                // For large files, also check the end where trailer usually is
                if (totalBytesRead >= maxBytesToRead && fileStream.CanSeek && fileStream.Length > maxBytesToRead)
                {
                    // Check the last 64KB where the trailer typically resides
                    fileStream.Position = Math.Max(0, fileStream.Length - 65536);
                    while (true)
                    {
                        bytesRead = await fileStream.ReadAsync(buffer, cancellationToken);
                        if (bytesRead == 0)
                            break;

                        if (ContainsSequence(buffer, bytesRead, encryptMarker))
                        {
                            _logger.LogInformation("Detected password-protected PDF (in trailer)");
                            return true;
                        }
                    }
                    break;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking PDF password protection, assuming not protected");
            return false;
        }
    }

    /// <summary>
    /// Checks if a byte array contains a specific sequence.
    /// </summary>
    private static bool ContainsSequence(byte[] buffer, int length, byte[] sequence)
    {
        if (length < sequence.Length)
            return false;

        for (var i = 0; i <= length - sequence.Length; i++)
        {
            var found = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (buffer[i + j] != sequence[j])
                {
                    found = false;
                    break;
                }
            }
            if (found)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects MIME type from file header bytes.
    /// </summary>
    private string? DetectMimeType(byte[] headerBytes, string fileName)
    {
        // Check PDF
        if (headerBytes.Length >= 4 &&
            headerBytes[0] == 0x25 && // %
            headerBytes[1] == 0x50 && // P
            headerBytes[2] == 0x44 && // D
            headerBytes[3] == 0x46)   // F
        {
            return "application/pdf";
        }

        // Check JPEG
        if (headerBytes.Length >= 3 &&
            headerBytes[0] == 0xFF &&
            headerBytes[1] == 0xD8 &&
            headerBytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // Check PNG
        if (headerBytes.Length >= 8 &&
            headerBytes[0] == 0x89 &&
            headerBytes[1] == 0x50 && // P
            headerBytes[2] == 0x4E && // N
            headerBytes[3] == 0x47 && // G
            headerBytes[4] == 0x0D &&
            headerBytes[5] == 0x0A &&
            headerBytes[6] == 0x1A &&
            headerBytes[7] == 0x0A)
        {
            return "image/png";
        }

        // Check HEIC/HEIF (ftyp box)
        // HEIC files start with ftyp box which can vary in size
        if (headerBytes.Length >= 12)
        {
            // Check for ftyp at offset 4
            if (headerBytes[4] == 0x66 && // f
                headerBytes[5] == 0x74 && // t
                headerBytes[6] == 0x79 && // y
                headerBytes[7] == 0x70)   // p
            {
                // Check for heic, heif, mif1 brands
                var brand = System.Text.Encoding.ASCII.GetString(headerBytes, 8, 4);
                if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
                {
                    return "image/heic";
                }
            }
        }

        // No fallback to extension - file must have valid magic bytes
        // This prevents renamed files (e.g., CSV renamed to .pdf) from being accepted
        _logger.LogWarning(
            "File {FileName} has invalid magic bytes. Header: {Header}",
            fileName,
            Convert.ToHexString(headerBytes[..Math.Min(8, headerBytes.Length)]));

        return null;
    }
}
