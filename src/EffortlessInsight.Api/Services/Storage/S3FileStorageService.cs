using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.Storage;

/// <summary>
/// S3 storage configuration options.
/// </summary>
public class S3StorageOptions
{
    public const string SectionName = "AWS:S3";

    public string BucketName { get; set; } = string.Empty;
    public string ReportsBucket { get; set; } = string.Empty;
    public string Region { get; set; } = "ap-south-1";
    public int UploadUrlExpiryMinutes { get; set; } = 15;
    public int DownloadUrlExpiryMinutes { get; set; } = 15;
}

/// <summary>
/// Result of generating a pre-signed upload URL.
/// </summary>
public record PresignedUploadResult(
    string Url,
    string Key,
    DateTime ExpiresAt,
    Dictionary<string, string> RequiredHeaders
);

/// <summary>
/// Result of generating a pre-signed download URL.
/// </summary>
public record PresignedDownloadResult(
    string Url,
    DateTime ExpiresAt
);

/// <summary>
/// Extended file storage service interface with pre-signed URL support.
/// </summary>
public interface IFileStorageServiceExtended : IFileStorageService
{
    /// <summary>
    /// Generates a pre-signed URL for direct client upload to S3.
    /// </summary>
    Task<PresignedUploadResult> GenerateUploadUrlAsync(
        Guid organizationId,
        Guid noticeId,
        string fileName,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a pre-signed URL for downloading a file.
    /// </summary>
    Task<PresignedDownloadResult> GenerateDownloadUrlAsync(
        string key,
        string? downloadFileName = null,
        int? expiryMinutes = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads an attachment to a notice.
    /// </summary>
    Task<string> UploadAttachmentAsync(
        Guid organizationId,
        Guid noticeId,
        Guid attachmentId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms that an upload completed successfully.
    /// </summary>
    Task<bool> ConfirmUploadAsync(
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the S3 key for a notice file.
    /// </summary>
    string GetNoticeKey(Guid organizationId, Guid noticeId, string extension);

    /// <summary>
    /// Gets the S3 key for an attachment.
    /// </summary>
    string GetAttachmentKey(Guid organizationId, Guid noticeId, Guid attachmentId, string extension);

    /// <summary>
    /// Uploads a report file to the reports bucket and returns a pre-signed download URL.
    /// </summary>
    /// <param name="fileName">Name of the file</param>
    /// <param name="contentType">MIME content type</param>
    /// <param name="content">File content as byte array</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Pre-signed download URL for the uploaded report</returns>
    Task<string> UploadReportAsync(
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of file storage service using AWS S3 with pre-signed URL support.
/// </summary>
public class S3FileStorageServiceImpl : IFileStorageServiceExtended
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3StorageOptions _options;
    private readonly ILogger<S3FileStorageServiceImpl> _logger;

    public S3FileStorageServiceImpl(
        IAmazonS3 s3Client,
        IOptions<S3StorageOptions> options,
        ILogger<S3FileStorageServiceImpl> logger)
    {
        _s3Client = s3Client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PresignedUploadResult> GenerateUploadUrlAsync(
        Guid organizationId,
        Guid noticeId,
        string fileName,
        string contentType,
        long contentLength,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var key = GetNoticeKey(organizationId, noticeId, extension);
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.UploadUrlExpiryMinutes);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = contentType
        };

        // Add metadata headers
        request.Metadata.Add("original-filename", SanitizeFileName(fileName));
        request.Metadata.Add("organization-id", organizationId.ToString());
        request.Metadata.Add("notice-id", noticeId.ToString());
        request.Metadata.Add("upload-timestamp", DateTime.UtcNow.ToString("O"));

        var url = await _s3Client.GetPreSignedURLAsync(request);

        _logger.LogInformation(
            "Generated pre-signed upload URL for notice {NoticeId} in org {OrganizationId}, key: {Key}",
            noticeId, organizationId, key);

        return new PresignedUploadResult(
            Url: url,
            Key: key,
            ExpiresAt: expiresAt,
            RequiredHeaders: new Dictionary<string, string>
            {
                ["Content-Type"] = contentType,
                ["x-amz-meta-original-filename"] = SanitizeFileName(fileName),
                ["x-amz-meta-organization-id"] = organizationId.ToString(),
                ["x-amz-meta-notice-id"] = noticeId.ToString()
            }
        );
    }

    /// <inheritdoc />
    public async Task<PresignedDownloadResult> GenerateDownloadUrlAsync(
        string key,
        string? downloadFileName = null,
        int? expiryMinutes = null,
        CancellationToken cancellationToken = default)
    {
        var expiry = expiryMinutes ?? _options.DownloadUrlExpiryMinutes;
        var expiresAt = DateTime.UtcNow.AddMinutes(expiry);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAt
        };

        // Set content disposition for download
        if (!string.IsNullOrEmpty(downloadFileName))
        {
            request.ResponseHeaderOverrides.ContentDisposition =
                $"attachment; filename=\"{SanitizeFileName(downloadFileName)}\"";
        }

        var url = await _s3Client.GetPreSignedURLAsync(request);

        _logger.LogDebug("Generated pre-signed download URL for key: {Key}", key);

        return new PresignedDownloadResult(
            Url: url,
            ExpiresAt: expiresAt
        );
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmUploadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3Client.GetObjectMetadataAsync(
                _options.BucketName,
                key,
                cancellationToken);

            return response.HttpStatusCode == System.Net.HttpStatusCode.OK;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> UploadAsync(Stream file, string fileName, string contentType)
    {
        // This method is for general uploads without organization context
        var key = $"uploads/{Guid.NewGuid()}/{SanitizeFileName(fileName)}";

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = file,
            ContentType = contentType
        };

        await _s3Client.PutObjectAsync(request);

        _logger.LogInformation("Uploaded file to S3: {Key}", key);

        return key;
    }

    /// <inheritdoc />
    public async Task<string> UploadAttachmentAsync(
        Guid organizationId,
        Guid noticeId,
        Guid attachmentId,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var key = GetAttachmentKey(organizationId, noticeId, attachmentId, extension);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType
        };

        request.Metadata.Add("original-filename", SanitizeFileName(fileName));
        request.Metadata.Add("organization-id", organizationId.ToString());
        request.Metadata.Add("notice-id", noticeId.ToString());
        request.Metadata.Add("attachment-id", attachmentId.ToString());

        await _s3Client.PutObjectAsync(request, cancellationToken);

        _logger.LogInformation(
            "Uploaded attachment {AttachmentId} for notice {NoticeId}, key: {Key}",
            attachmentId, noticeId, key);

        return key;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string fileUrl)
    {
        // Extract key from URL or use as key directly
        var key = ExtractKeyFromUrl(fileUrl);

        var response = await _s3Client.GetObjectAsync(_options.BucketName, key);

        return response.ResponseStream;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string fileUrl)
    {
        var key = ExtractKeyFromUrl(fileUrl);

        await _s3Client.DeleteObjectAsync(_options.BucketName, key);

        _logger.LogInformation("Deleted file from S3: {Key}", key);
    }

    /// <inheritdoc />
    public async Task<string> GetPresignedUrlAsync(string fileUrl, TimeSpan expiry)
    {
        var key = ExtractKeyFromUrl(fileUrl);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        };

        return await _s3Client.GetPreSignedURLAsync(request);
    }

    /// <inheritdoc />
    public string GetNoticeKey(Guid organizationId, Guid noticeId, string extension)
    {
        // Ensure extension starts with a dot
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        return $"{organizationId}/notices/{noticeId}/original{extension}";
    }

    /// <inheritdoc />
    public string GetAttachmentKey(Guid organizationId, Guid noticeId, Guid attachmentId, string extension)
    {
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        return $"{organizationId}/notices/{noticeId}/attachments/{attachmentId}{extension}";
    }

    /// <inheritdoc />
    public async Task<string> UploadReportAsync(
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        // Generate unique key for the report
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var key = $"reports/{timestamp}/{SanitizeFileName(fileName)}";

        var request = new PutObjectRequest
        {
            BucketName = _options.ReportsBucket,
            Key = key,
            InputStream = new MemoryStream(content),
            ContentType = contentType
        };

        request.Metadata.Add("generated-at", DateTime.UtcNow.ToString("O"));
        request.Metadata.Add("original-filename", SanitizeFileName(fileName));

        await _s3Client.PutObjectAsync(request, cancellationToken);

        _logger.LogInformation(
            "Uploaded report to S3 reports bucket: {Key}, size: {Size} bytes",
            key, content.Length);

        // Generate pre-signed URL for download (1 hour expiry)
        var downloadRequest = new GetPreSignedUrlRequest
        {
            BucketName = _options.ReportsBucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(60),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{SanitizeFileName(fileName)}\""
            }
        };

        var downloadUrl = await _s3Client.GetPreSignedURLAsync(downloadRequest);

        return downloadUrl;
    }

    /// <summary>
    /// Extracts the S3 key from a URL or returns the input if already a key.
    /// </summary>
    private string ExtractKeyFromUrl(string urlOrKey)
    {
        if (string.IsNullOrEmpty(urlOrKey))
            return urlOrKey;

        // If it's a full S3 URL, extract the key
        if (urlOrKey.StartsWith("http://") || urlOrKey.StartsWith("https://"))
        {
            var uri = new Uri(urlOrKey);
            // Remove leading slash from path
            return uri.AbsolutePath.TrimStart('/');
        }

        // Already a key
        return urlOrKey;
    }

    /// <summary>
    /// Sanitizes a filename for safe storage.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "unnamed";

        // Remove path components
        fileName = Path.GetFileName(fileName);

        // Replace dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars()
            .Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|'])
            .ToArray();

        foreach (var c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }

        // Limit length
        if (fileName.Length > 200)
        {
            var ext = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);
            fileName = name[..(200 - ext.Length)] + ext;
        }

        return fileName;
    }
}
