using System.Security.Claims;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.WhatsApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// API endpoints for WhatsApp integration.
/// </summary>
[ApiController]
[Route("api/v1/whatsapp")]
[Authorize]
public class WhatsAppController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWhatsAppVerificationService _verificationService;
    private readonly IWhatsAppSessionService _sessionService;
    private readonly IMetaWhatsAppClient _client;
    private readonly ILogger<WhatsAppController> _logger;

    public WhatsAppController(
        ApplicationDbContext db,
        IWhatsAppVerificationService verificationService,
        IWhatsAppSessionService sessionService,
        IMetaWhatsAppClient client,
        ILogger<WhatsAppController> logger)
    {
        _db = db;
        _verificationService = verificationService;
        _sessionService = sessionService;
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Get WhatsApp connection status.
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<WhatsAppStatusResponse>> GetStatus(CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync([userId], ct);

        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        return Ok(new WhatsAppStatusResponse(
            Linked: user.WhatsAppVerified,
            PhoneNumber: user.WhatsAppPhoneNumber != null
                ? _client.MaskPhoneNumber(user.WhatsAppPhoneNumber)
                : null,
            LinkedAt: user.WhatsAppVerifiedAt,
            OptedIn: user.WhatsAppOptedIn,
            LastMessageAt: user.WhatsAppLastMessageAt
        ));
    }

    /// <summary>
    /// Initiate WhatsApp linking.
    /// </summary>
    [HttpPost("link/request")]
    public async Task<ActionResult<WhatsAppLinkResponse>> RequestLink(
        [FromBody] WhatsAppLinkRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new { message = "Phone number is required" });
        }

        var (success, expiresAt, message) = await _verificationService.InitiateVerificationAsync(
            userId,
            request.PhoneNumber,
            ct);

        if (!success)
        {
            return BadRequest(new WhatsAppLinkResponse(false, null, message));
        }

        return Ok(new WhatsAppLinkResponse(true, expiresAt, "Verification code sent to app"));
    }

    /// <summary>
    /// Verify WhatsApp linking code (for app verification flow).
    /// </summary>
    [HttpPost("link/verify")]
    public async Task<IActionResult> VerifyLink(
        [FromBody] VerifyCodeRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();

        // Get pending verification for this user
        var user = await _db.Users.FindAsync([userId], ct);
        if (user?.WhatsAppPhoneNumber != null)
        {
            return BadRequest(new { message = "WhatsApp already linked" });
        }

        var verification = await _db.WhatsAppVerifications
            .Where(v =>
                v.UserId == userId &&
                !v.IsVerified &&
                v.ExpiresAt > DateTime.UtcNow &&
                v.DeletedAt == null)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (verification == null)
        {
            return BadRequest(new { message = "No pending verification found. Please request a new code." });
        }

        var (success, _, message) = await _verificationService.VerifyCodeAsync(
            verification.Id,
            request.Code,
            ct);

        if (!success)
        {
            return BadRequest(new { message });
        }

        // Link session if it exists
        var session = await _sessionService.GetSessionByPhoneAsync(verification.PhoneNumber, ct);
        if (session != null)
        {
            await _sessionService.LinkSessionToUserAsync(session.Id, userId, ct);
        }
        else
        {
            // Update user directly
            user!.WhatsAppPhoneNumber = verification.PhoneNumber;
            user.WhatsAppVerified = true;
            user.WhatsAppVerifiedAt = DateTime.UtcNow;
            user.WhatsAppOptedIn = true;
            user.WhatsAppOptedInAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { message = "WhatsApp linked successfully" });
    }

    /// <summary>
    /// Disconnect WhatsApp.
    /// </summary>
    [HttpPost("unlink")]
    public async Task<IActionResult> Unlink(CancellationToken ct)
    {
        var userId = GetUserId();

        await _sessionService.UnlinkSessionAsync(userId, ct);

        return Ok(new { message = "WhatsApp unlinked successfully" });
    }

    /// <summary>
    /// Get WhatsApp notification preferences.
    /// </summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<WhatsAppPreferencesResponse>> GetPreferences(CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync([userId], ct);

        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        // Get preferences from user's Preferences JSON
        var prefs = user.Preferences ?? new Dictionary<string, object>();

        return Ok(new WhatsAppPreferencesResponse(
            DeadlineReminders: GetPref(prefs, "whatsapp_deadline_reminders", true),
            HighRiskAlerts: GetPref(prefs, "whatsapp_high_risk_alerts", true),
            TaskAssignments: GetPref(prefs, "whatsapp_task_assignments", true),
            DailyDigest: GetPref(prefs, "whatsapp_daily_digest", false)
        ));
    }

    /// <summary>
    /// Update WhatsApp notification preferences.
    /// </summary>
    [HttpPatch("preferences")]
    public async Task<ActionResult<WhatsAppPreferencesResponse>> UpdatePreferences(
        [FromBody] WhatsAppPreferencesRequest request,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync([userId], ct);

        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        var prefs = user.Preferences ?? new Dictionary<string, object>();

        if (request.DeadlineReminders.HasValue)
            prefs["whatsapp_deadline_reminders"] = request.DeadlineReminders.Value;

        if (request.HighRiskAlerts.HasValue)
            prefs["whatsapp_high_risk_alerts"] = request.HighRiskAlerts.Value;

        if (request.TaskAssignments.HasValue)
            prefs["whatsapp_task_assignments"] = request.TaskAssignments.Value;

        if (request.DailyDigest.HasValue)
            prefs["whatsapp_daily_digest"] = request.DailyDigest.Value;

        user.Preferences = prefs;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new WhatsAppPreferencesResponse(
            DeadlineReminders: GetPref(prefs, "whatsapp_deadline_reminders", true),
            HighRiskAlerts: GetPref(prefs, "whatsapp_high_risk_alerts", true),
            TaskAssignments: GetPref(prefs, "whatsapp_task_assignments", true),
            DailyDigest: GetPref(prefs, "whatsapp_daily_digest", false)
        ));
    }

    /// <summary>
    /// Opt-in/opt-out of WhatsApp notifications.
    /// </summary>
    [HttpPost("opt-in")]
    public async Task<IActionResult> OptIn([FromBody] OptInRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync([userId], ct);

        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        if (!user.WhatsAppVerified)
        {
            return BadRequest(new { message = "WhatsApp not linked" });
        }

        user.WhatsAppOptedIn = request.OptIn;
        user.WhatsAppOptedInAt = request.OptIn ? DateTime.UtcNow : null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            optedIn = user.WhatsAppOptedIn,
            message = request.OptIn
                ? "Opted in to WhatsApp notifications"
                : "Opted out of WhatsApp notifications"
        });
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user ID");
        }

        return userId;
    }

    private static bool GetPref(Dictionary<string, object> prefs, string key, bool defaultValue)
    {
        if (prefs.TryGetValue(key, out var value))
        {
            if (value is bool boolValue)
                return boolValue;
            if (value is string strValue && bool.TryParse(strValue, out var parsed))
                return parsed;
        }
        return defaultValue;
    }
}

public record VerifyCodeRequest(string Code);
public record OptInRequest(bool OptIn);
