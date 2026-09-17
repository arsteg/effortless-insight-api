using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Ca;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.DTOs.Ca;
using EffortlessInsight.Api.Services.Auth;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Organizations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace EffortlessInsight.Api.Services.Ca;

/// <summary>
/// Service for managing CA profiles.
/// </summary>
public class CaProfileService : ICaProfileService
{
    /// <summary>
    /// Plan code assigned to every CA organization. CAs use the platform free of charge,
    /// so their organization is put on this zero-cost plan as soon as it is created.
    /// </summary>
    private const string CaOperatorPlanCode = "ca_operator";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IJwtService _jwtService;
    private readonly IGstinValidatorService _gstinValidator;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<CaProfileService> _logger;

    public CaProfileService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IJwtService jwtService,
        IGstinValidatorService gstinValidator,
        ISubscriptionService subscriptionService,
        ILogger<CaProfileService> logger)
    {
        _db = db;
        _userManager = userManager;
        _jwtService = jwtService;
        _gstinValidator = gstinValidator;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task<CaRegisterResult> RegisterCaAsync(CaRegisterRequest request, CancellationToken ct = default)
    {
        // Check if email already exists
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return new CaRegisterResult(false, ErrorCode: "EMAIL_EXISTS", ErrorMessage: "Email already registered");
        }

        // Use execution strategy for transaction support with NpgsqlRetryingExecutionStrategy
        var strategy = _db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Create user
                var user = new ApplicationUser
                {
                    Email = request.Email,
                    UserName = request.Email,
                    Name = request.Name,
                    Mobile = request.Mobile,
                    MobileNormalized = NormalizeMobile(request.Mobile),
                    IsCa = true,
                    Role = "ca",
                    EmailConfirmed = false, // Will need to verify email
                    TermsAccepted = request.AcceptTerms,
                    TermsAcceptedAt = DateTime.UtcNow
                };

                var result = await _userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                    return new CaRegisterResult(false, ErrorCode: "REGISTRATION_FAILED", ErrorMessage: errors);
                }

                // Create CA profile
                var caProfile = new CaProfile
                {
                    UserId = user.Id,
                    FirmName = request.FirmName,
                    MembershipNumber = request.MembershipNumber?.ToUpperInvariant(),
                    IsVerified = false,
                    Status = CaProfileStatus.Active
                };

                _db.CaProfiles.Add(caProfile);
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                _logger.LogInformation("CA registered: {UserId}, Email: {Email}", user.Id, user.Email);

                return new CaRegisterResult(true, UserId: user.Id, CaProfileId: caProfile.Id);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to register CA: {Email}", request.Email);
                // In development, return the actual error message for debugging
                var errorMessage = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
                    ? $"Registration error: {ex.Message}"
                    : "An error occurred during registration";
                return new CaRegisterResult(false, ErrorCode: "REGISTRATION_ERROR", ErrorMessage: errorMessage);
            }
        });
    }

    public async Task<CaProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.CaProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);
    }

    public async Task<CaProfileDto?> GetProfileDtoAsync(Guid userId, CancellationToken ct = default)
    {
        var profile = await _db.CaProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (profile == null)
            return null;

        // Get client counts
        var activeClientCount = await _db.CaClientRelationships
            .CountAsync(r =>
                r.CaUserId == userId &&
                r.Status == CaClientRelationshipStatus.Active,
                ct);

        var pendingInvitationCount = await _db.CaInvitations
            .CountAsync(i =>
                i.InviterUserId == userId &&
                i.Status == CaInvitationStatus.Pending,
                ct);

        return new CaProfileDto(
            Id: profile.Id,
            UserId: profile.UserId,
            UserName: profile.User.Name,
            UserEmail: profile.User.Email!,
            FirmName: profile.FirmName,
            MembershipNumber: profile.MembershipNumber,
            IsVerified: profile.IsVerified,
            VerifiedAt: profile.VerifiedAt,
            Status: profile.Status,
            CreatedAt: profile.CreatedAt,
            ActiveClientCount: activeClientCount,
            PendingInvitationCount: pendingInvitationCount
        );
    }

    public async Task<CaProfile> UpdateAsync(Guid userId, UpdateCaProfileRequest request, CancellationToken ct = default)
    {
        var profile = await _db.CaProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new InvalidOperationException("CA profile not found");

        if (request.FirmName != null)
            profile.FirmName = request.FirmName;

        if (request.MembershipNumber != null)
            profile.MembershipNumber = request.MembershipNumber.ToUpperInvariant();

        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("CA profile updated: {UserId}", userId);
        return profile;
    }

    public async Task<bool> IsCaAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.Users
            .AnyAsync(u => u.Id == userId && u.IsCa, ct);
    }

    public async Task<CreateCaOrganizationResponse> CreateOrganizationAsync(
        Guid userId,
        CreateCaOrganizationRequest request,
        string ipAddress,
        string userAgent,
        CancellationToken ct = default)
    {
        // Get user
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new KeyNotFoundException("USER_NOT_FOUND");

        // Verify user is a CA
        if (!user.IsCa)
        {
            throw new InvalidOperationException("NOT_CA");
        }

        // Check if user already has an organization
        if (user.OrganizationId.HasValue)
        {
            throw new InvalidOperationException("ALREADY_HAS_ORGANIZATION");
        }

        // Check if organization name already exists
        var normalizedName = request.Name.Trim().ToLowerInvariant();
        if (await _db.Organizations.AnyAsync(o => o.NameNormalized == normalizedName && o.DeletedAt == null, ct))
        {
            throw new InvalidOperationException("ORG_NAME_EXISTS");
        }

        // Validate GSTIN if provided
        OrganizationGstin? gstin = null;
        if (!string.IsNullOrWhiteSpace(request.Gstin))
        {
            var gstinResult = _gstinValidator.Validate(request.Gstin);
            if (!gstinResult.IsValid)
            {
                throw new InvalidOperationException($"INVALID_GSTIN: {gstinResult.ErrorMessage}");
            }

            // Check if GSTIN already exists
            if (await _gstinValidator.ExistsAsync(request.Gstin))
            {
                throw new InvalidOperationException("GSTIN_EXISTS");
            }

            // Get state name from database
            var stateName = await _gstinValidator.GetStateNameAsync(gstinResult.StateCode!) ?? gstinResult.StateName!;

            gstin = new OrganizationGstin
            {
                Gstin = gstinResult.Gstin!,
                StateCode = gstinResult.StateCode!,
                StateName = stateName,
                IsPrimary = true,
                Status = "active"
            };
        }

        // Use execution strategy for transaction support
        var strategy = _db.Database.CreateExecutionStrategy();

        var response = await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Create owner membership entity
                var membership = new OrganizationMember
                {
                    UserId = userId,
                    Role = "owner",
                    IsExternal = false,
                    Status = "active",
                    JoinedAt = DateTime.UtcNow
                };

                // Create organization
                var organization = new Organization
                {
                    Name = request.Name.Trim(),
                    NameNormalized = normalizedName,
                    LegalName = request.LegalName?.Trim(),
                    Industry = request.Industry,
                    State = request.State,
                    City = request.City,
                    // Overwritten with "active" once the free CA operator plan is
                    // activated below; stays "none" only if that activation fails.
                    SubscriptionStatus = "none",
                    TrialEndsAt = null,
                    Settings = new Dictionary<string, object>
                    {
                        ["default_reminder_days"] = new[] { 7, 3, 1 },
                        ["notification_email"] = true,
                        ["notification_sms"] = true,
                        ["allow_ca_access"] = true,
                        ["require_response_approval"] = false,
                        ["timezone"] = "Asia/Kolkata",
                        ["language"] = "en",
                        ["date_format"] = "DD/MM/YYYY"
                    },
                    Members = { membership }
                };

                // Add GSTIN if provided
                if (gstin != null)
                {
                    organization.OrganizationGstins.Add(gstin);
                }

                _db.Organizations.Add(organization);

                // Save to get the organization ID
                await _db.SaveChangesAsync(ct);

                // Update user's default organization
                user.OrganizationId = organization.Id;
                user.Role = "owner";
                await _db.SaveChangesAsync(ct);

                // Generate new tokens with organization context
                var accessToken = _jwtService.GenerateAccessToken(user, organization, "owner");
                var (refreshToken, jti, expiresAt) = _jwtService.GenerateRefreshToken();

                // Create session
                var session = new UserSession
                {
                    UserId = user.Id,
                    RefreshTokenHash = HashToken(refreshToken),
                    RefreshTokenJti = jti,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Platform = "web",
                    ExpiresAt = expiresAt,
                    LastActiveAt = DateTime.UtcNow,
                    OrganizationId = organization.Id,
                    Role = "owner",
                    IsExternal = false
                };

                _db.UserSessions.Add(session);
                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);

                _logger.LogInformation(
                    "CA organization {OrganizationId} created by user {UserId}",
                    organization.Id, userId);

                var expiresIn = _jwtService.GetAccessTokenExpiryMinutes() * 60;

                return new CreateCaOrganizationResponse(
                    OrganizationId: organization.Id,
                    Name: organization.Name,
                    LegalName: organization.LegalName,
                    Gstin: gstin?.Gstin,
                    State: organization.State!,
                    City: organization.City,
                    AccessToken: accessToken,
                    RefreshToken: refreshToken,
                    ExpiresIn: expiresIn
                );
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to create CA organization for user {UserId}", userId);
                throw;
            }
        });

        // Free access is granted per-CA by an admin. If it has already been granted, put
        // the new organization on the zero-cost CA operator plan now; otherwise leave the
        // org unsubscribed and let the normal subscription gates run, which surfaces the
        // "awaiting approval" state.
        //
        // Deliberately outside the execution strategy above: this does its own
        // SaveChanges, and re-running it on a transient retry would be wasteful. It is
        // idempotent, so a later retry of the whole request is still safe.
        var caProfile = await _db.CaProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, ct);

        if (caProfile?.AllowFreePlan == true)
        {
            try
            {
                await _subscriptionService.ActivateFreePlanAsync(
                    response.OrganizationId,
                    CaOperatorPlanCode,
                    allowCaOperatorPlan: true);
            }
            catch (Exception ex)
            {
                // Never fail organization creation over this - the org exists and the user
                // is logged in. Surfaces as the subscription gate until it is put right.
                _logger.LogError(
                    ex,
                    "Failed to activate '{PlanCode}' for CA organization {OrganizationId}; the CA will be treated as unsubscribed",
                    CaOperatorPlanCode,
                    response.OrganizationId);
            }
        }

        return response;
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeMobile(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
            return null;

        // Remove any non-digit characters and normalize
        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        return digits.Length == 10 ? digits : null;
    }
}
