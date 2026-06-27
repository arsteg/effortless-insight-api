using System.IO.Compression;
using EffortlessInsight.Api.Services.Storage;

namespace EffortlessInsight.Api.Services.Notices;

/// <summary>
/// Service for processing ZIP file uploads containing multiple notices.
/// </summary>
public interface IZipProcessingService
{
    /// <summary>
    /// Extract and process notices from a ZIP file.
    /// </summary>
    Task<ZipProcessingResult> ProcessZipUploadAsync(
        Stream zipStream,
        string fileName,
        Guid organizationId,
        Guid userId,
        string? gstin,
        string[]? tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a ZIP file before processing.
    /// </summary>
    ZipValidationResult ValidateZipFile(Stream zipStream, string fileName);
}

public class ZipProcessingService : IZipProcessingService
{
    private readonly INoticeServiceExtended _noticeService;
    private readonly IFileValidationService _fileValidationService;
    private readonly ILogger<ZipProcessingService> _logger;

    // Configuration
    private const int MaxFilesPerZip = 50;
    private const long MaxZipSize = 500 * 1024 * 1024; // 500 MB
    private const long MaxExtractedSize = 1024 * 1024 * 1024; // 1 GB total extracted
    private const long MaxSingleFileSize = 50 * 1024 * 1024; // 50 MB per file

    // Allowed file extensions for notices
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".tiff", ".tif", ".doc", ".docx", ".xls", ".xlsx"
    };

    public ZipProcessingService(
        INoticeServiceExtended noticeService,
        IFileValidationService fileValidationService,
        ILogger<ZipProcessingService> logger)
    {
        _noticeService = noticeService;
        _fileValidationService = fileValidationService;
        _logger = logger;
    }

    public ZipValidationResult ValidateZipFile(Stream zipStream, string fileName)
    {
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ZipValidationResult.Invalid("INVALID_FILE_TYPE", "File must be a ZIP archive");
        }

        if (zipStream.Length > MaxZipSize)
        {
            return ZipValidationResult.Invalid("FILE_TOO_LARGE",
                $"ZIP file exceeds maximum size of {MaxZipSize / (1024 * 1024)} MB");
        }

        try
        {
            zipStream.Position = 0;
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            var validEntries = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name) && !e.FullName.EndsWith('/'))
                .ToList();

            if (validEntries.Count == 0)
            {
                return ZipValidationResult.Invalid("EMPTY_ARCHIVE", "ZIP file contains no valid files");
            }

            if (validEntries.Count > MaxFilesPerZip)
            {
                return ZipValidationResult.Invalid("TOO_MANY_FILES",
                    $"ZIP contains {validEntries.Count} files, maximum allowed is {MaxFilesPerZip}");
            }

            // Check total extracted size (zip bomb protection)
            var totalSize = validEntries.Sum(e => e.Length);
            if (totalSize > MaxExtractedSize)
            {
                return ZipValidationResult.Invalid("EXTRACTED_SIZE_TOO_LARGE",
                    $"Total extracted size exceeds maximum of {MaxExtractedSize / (1024 * 1024)} MB");
            }

            // Check individual file sizes and extensions
            var unsupportedFiles = new List<string>();
            var oversizedFiles = new List<string>();

            foreach (var entry in validEntries)
            {
                var ext = Path.GetExtension(entry.Name);
                if (!AllowedExtensions.Contains(ext))
                {
                    unsupportedFiles.Add(entry.Name);
                }

                if (entry.Length > MaxSingleFileSize)
                {
                    oversizedFiles.Add(entry.Name);
                }
            }

            if (oversizedFiles.Count > 0)
            {
                return ZipValidationResult.Invalid("FILES_TOO_LARGE",
                    $"Files exceed maximum size: {string.Join(", ", oversizedFiles.Take(5))}");
            }

            // Filter to only supported files
            var supportedEntries = validEntries
                .Where(e => AllowedExtensions.Contains(Path.GetExtension(e.Name)))
                .ToList();

            if (supportedEntries.Count == 0)
            {
                return ZipValidationResult.Invalid("NO_SUPPORTED_FILES",
                    $"No supported file types found. Allowed: {string.Join(", ", AllowedExtensions)}");
            }

            return ZipValidationResult.Valid(
                supportedEntries.Count,
                unsupportedFiles.Count,
                unsupportedFiles.Take(10).ToList());
        }
        catch (InvalidDataException)
        {
            return ZipValidationResult.Invalid("INVALID_ZIP", "File is not a valid ZIP archive");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating ZIP file: {FileName}", fileName);
            return ZipValidationResult.Invalid("VALIDATION_ERROR", "Failed to validate ZIP file");
        }
        finally
        {
            zipStream.Position = 0;
        }
    }

    public async Task<ZipProcessingResult> ProcessZipUploadAsync(
        Stream zipStream,
        string fileName,
        Guid organizationId,
        Guid userId,
        string? gstin,
        string[]? tags,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ZipFileResult>();
        var processedCount = 0;
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;

        _logger.LogInformation(
            "Processing ZIP upload: {FileName} for org {OrgId} by user {UserId}",
            fileName, organizationId, userId);

        try
        {
            zipStream.Position = 0;
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            var validEntries = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name) &&
                           !e.FullName.EndsWith('/') &&
                           AllowedExtensions.Contains(Path.GetExtension(e.Name)))
                .OrderBy(e => e.FullName)
                .ToList();

            foreach (var entry in validEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedCount++;

                try
                {
                    // Extract to memory stream
                    using var entryStream = entry.Open();
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream, cancellationToken);
                    memoryStream.Position = 0;

                    // Determine content type
                    var contentType = GetContentType(entry.Name);

                    // Upload the notice
                    var uploadResult = await _noticeService.UploadAsync(
                        memoryStream,
                        entry.Name,
                        contentType,
                        organizationId,
                        userId,
                        gstin,
                        tags?.ToList(),
                        cancellationToken);

                    if (uploadResult.Success)
                    {
                        successCount++;
                        results.Add(new ZipFileResult(
                            FileName: entry.Name,
                            FullPath: entry.FullName,
                            Success: true,
                            NoticeId: uploadResult.NoticeId,
                            Status: uploadResult.Status,
                            ErrorCode: null,
                            ErrorMessage: null,
                            IsDuplicate: uploadResult.DuplicateWarning != null));
                    }
                    else
                    {
                        failureCount++;
                        results.Add(new ZipFileResult(
                            FileName: entry.Name,
                            FullPath: entry.FullName,
                            Success: false,
                            NoticeId: null,
                            Status: null,
                            ErrorCode: uploadResult.ErrorCode,
                            ErrorMessage: uploadResult.ErrorMessage,
                            IsDuplicate: false));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process ZIP entry: {EntryName}", entry.FullName);
                    failureCount++;
                    results.Add(new ZipFileResult(
                        FileName: entry.Name,
                        FullPath: entry.FullName,
                        Success: false,
                        NoticeId: null,
                        Status: null,
                        ErrorCode: "PROCESSING_ERROR",
                        ErrorMessage: "Failed to process file from archive",
                        IsDuplicate: false));
                }
            }

            // Count skipped (unsupported) files
            skippedCount = archive.Entries
                .Count(e => !string.IsNullOrEmpty(e.Name) &&
                           !e.FullName.EndsWith('/') &&
                           !AllowedExtensions.Contains(Path.GetExtension(e.Name)));

            _logger.LogInformation(
                "ZIP processing complete: {FileName} - {Success}/{Total} succeeded, {Skipped} skipped",
                fileName, successCount, processedCount, skippedCount);

            return new ZipProcessingResult(
                Success: true,
                ZipFileName: fileName,
                TotalFilesInZip: processedCount + skippedCount,
                ProcessedCount: processedCount,
                SuccessCount: successCount,
                FailureCount: failureCount,
                SkippedCount: skippedCount,
                Results: results,
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ZIP processing cancelled: {FileName}", fileName);
            return new ZipProcessingResult(
                Success: false,
                ZipFileName: fileName,
                TotalFilesInZip: 0,
                ProcessedCount: processedCount,
                SuccessCount: successCount,
                FailureCount: failureCount,
                SkippedCount: skippedCount,
                Results: results,
                ErrorCode: "CANCELLED",
                ErrorMessage: "Processing was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ZIP file: {FileName}", fileName);
            return new ZipProcessingResult(
                Success: false,
                ZipFileName: fileName,
                TotalFilesInZip: 0,
                ProcessedCount: processedCount,
                SuccessCount: successCount,
                FailureCount: failureCount,
                SkippedCount: skippedCount,
                Results: results,
                ErrorCode: "PROCESSING_FAILED",
                ErrorMessage: "Failed to process ZIP archive");
        }
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tiff" or ".tif" => "image/tiff",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}

#region Result Types

public record ZipValidationResult(
    bool IsValid,
    int ValidFileCount,
    int SkippedFileCount,
    List<string> SkippedFiles,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ZipValidationResult Valid(int validCount, int skippedCount, List<string> skippedFiles) =>
        new(true, validCount, skippedCount, skippedFiles, null, null);

    public static ZipValidationResult Invalid(string errorCode, string errorMessage) =>
        new(false, 0, 0, new List<string>(), errorCode, errorMessage);
}

public record ZipProcessingResult(
    bool Success,
    string ZipFileName,
    int TotalFilesInZip,
    int ProcessedCount,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    List<ZipFileResult> Results,
    string? ErrorCode,
    string? ErrorMessage);

public record ZipFileResult(
    string FileName,
    string FullPath,
    bool Success,
    Guid? NoticeId,
    string? Status,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsDuplicate);

#endregion
