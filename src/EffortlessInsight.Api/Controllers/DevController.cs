using EffortlessInsight.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers;

/// <summary>
/// Development-only endpoints for testing and debugging.
/// These endpoints are only available when the application is running in Development environment.
/// </summary>
[ApiController]
[Route("api/v1/dev")]
public class DevController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DevController> _logger;

    public DevController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        ILogger<DevController> logger)
    {
        _dbContext = dbContext;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Verify email for a user (Development/Local only)
    /// </summary>
    [HttpPost("verify-email/{email}")]
    public async Task<IActionResult> VerifyEmail(string email)
    {
        if (!IsDevEnvironment())
        {
            return NotFound();
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return NotFound(new { error = "User not found", email });
        }

        user.EmailConfirmed = true;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Email verified for user {Email} via dev endpoint", email);

        return Ok(new {
            message = "Email verified successfully",
            email = user.Email,
            emailConfirmed = user.EmailConfirmed
        });
    }

    /// <summary>
    /// Verify all unverified emails (Development/Local only)
    /// </summary>
    [HttpPost("verify-all-emails")]
    public async Task<IActionResult> VerifyAllEmails()
    {
        if (!IsDevEnvironment())
        {
            return NotFound();
        }

        var unverifiedUsers = await _dbContext.Users
            .Where(u => !u.EmailConfirmed)
            .ToListAsync();

        foreach (var user in unverifiedUsers)
        {
            user.EmailConfirmed = true;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Verified {Count} user emails via dev endpoint", unverifiedUsers.Count);

        return Ok(new {
            message = $"Verified {unverifiedUsers.Count} user emails",
            verifiedEmails = unverifiedUsers.Select(u => u.Email).ToList()
        });
    }

    /// <summary>
    /// List all users (Development/Local only)
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        if (!IsDevEnvironment())
        {
            return NotFound();
        }

        var users = await _dbContext.Users
            .Select(u => new {
                u.Id,
                u.Email,
                u.Name,
                u.EmailConfirmed,
                u.IsActive,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    /// <summary>
    /// Check if running in Development or Local environment
    /// </summary>
    private bool IsDevEnvironment()
    {
        return _environment.IsDevelopment() ||
               _environment.EnvironmentName.Equals("Local", StringComparison.OrdinalIgnoreCase);
    }
}
