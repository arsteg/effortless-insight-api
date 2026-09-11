using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// The signed-in user's own profile image.
///
/// The web client has always called POST/DELETE /users/avatar, but no such
/// endpoint existed — every upload 404'd and quietly showed "Failed to upload
/// avatar" (TC-MOB-064). `AvatarUrl` was storable on the user but there was no
/// way to put an image behind it.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/users")]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Avatars are displayed at most a couple of hundred pixels; anything
    /// larger is the client failing to resize before upload.
    /// </summary>
    private const long MaxAvatarBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Allow-list rather than a block-list: an avatar is rendered in an
    /// <img>-equivalent everywhere, so an SVG here would be a stored-XSS
    /// vector on the web client.
    /// </summary>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp", "image/heic", "image/heif",
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _storage;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ApplicationDbContext dbContext,
        IFileStorageService storage,
        ILogger<UsersController> logger)
    {
        _dbContext = dbContext;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Upload or replace the current user's avatar.</summary>
    [HttpPost("avatar")]
    [ProducesResponseType(typeof(ApiResponse<AvatarResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(MaxAvatarBytes)]
    public async Task<IActionResult> UploadAvatar(IFormFile avatar)
    {
        if (avatar == null || avatar.Length == 0)
        {
            return BadRequest(new ApiErrorResponse(false, "NO_FILE", "No image provided"));
        }

        if (avatar.Length > MaxAvatarBytes)
        {
            return BadRequest(new ApiErrorResponse(
                false, "FILE_TOO_LARGE", "Image must be 5MB or smaller"));
        }

        if (!AllowedContentTypes.Contains(avatar.ContentType))
        {
            return BadRequest(new ApiErrorResponse(
                false, "UNSUPPORTED_TYPE", "Image must be a JPEG, PNG, WebP or HEIC"));
        }

        var userId = GetCurrentUserId();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
        }

        var previousUrl = user.AvatarUrl;

        try
        {
            // Keyed by user id so a replacement cannot collide with another
            // user's file, with the extension preserved for content sniffing.
            var extension = Path.GetExtension(avatar.FileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10) extension = ".jpg";
            var storedName = $"avatars/{userId}{extension}";

            using var stream = avatar.OpenReadStream();
            var url = await _storage.UploadAsync(stream, storedName, avatar.ContentType);

            user.AvatarUrl = url;
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Only after the new one is safely stored, and never if it is the
            // same object — a failure here must not cost the user their avatar.
            if (!string.IsNullOrEmpty(previousUrl) && previousUrl != url)
            {
                try { await _storage.DeleteAsync(previousUrl); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove previous avatar for user {UserId}", userId);
                }
            }

            _logger.LogInformation("User {UserId} updated their avatar", userId);
            return Ok(new ApiResponse<AvatarResponse>(true, new AvatarResponse(url)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload avatar for user {UserId}", userId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ApiErrorResponse(false, "UPLOAD_FAILED", "Could not save the image. Please try again."));
        }
    }

    /// <summary>Remove the current user's avatar, falling back to initials.</summary>
    [HttpDelete("avatar")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAvatar()
    {
        var userId = GetCurrentUserId();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null)
        {
            return NotFound(new ApiErrorResponse(false, "USER_NOT_FOUND", "User not found"));
        }

        var url = user.AvatarUrl;
        user.AvatarUrl = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // The record is what the UI reads, so clearing it is the part that must
        // succeed; an orphaned object is preferable to a half-removed avatar.
        if (!string.IsNullOrEmpty(url))
        {
            try { await _storage.DeleteAsync(url); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove avatar object for user {UserId}", userId);
            }
        }

        return NoContent();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value
            ?? throw new UnauthorizedAccessException("User ID not found in token");

        return Guid.Parse(userIdClaim);
    }
}

/// <summary>The stored avatar's URL, for the client to render immediately.</summary>
public record AvatarResponse(string AvatarUrl);
