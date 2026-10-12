namespace EffortlessInsight.Api.Services;

/// <summary>
/// Turns a stored <c>AvatarUrl</c> into something a browser can load.
///
/// Uploaded avatars are stored as S3 object keys (e.g. "uploads/{guid}/avatars_{id}.jpg")
/// in a private bucket, so the raw value is not a usable image src. OAuth providers'
/// pictures, by contrast, are already full public URLs and are passed through untouched.
/// </summary>
public static class AvatarUrlExtensions
{
    /// <summary>
    /// Long enough that an open tab keeps showing the image, short enough that a leaked
    /// link stops working. The client re-fetches the profile on every load.
    /// </summary>
    private static readonly TimeSpan AvatarUrlExpiry = TimeSpan.FromHours(24);

    public static async Task<string?> ResolveAvatarUrlAsync(this IFileStorageService storage, string? storedAvatarUrl)
    {
        if (string.IsNullOrWhiteSpace(storedAvatarUrl))
            return null;

        if (storedAvatarUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            storedAvatarUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return storedAvatarUrl;

        return await storage.GetPresignedUrlAsync(storedAvatarUrl, AvatarUrlExpiry);
    }
}
