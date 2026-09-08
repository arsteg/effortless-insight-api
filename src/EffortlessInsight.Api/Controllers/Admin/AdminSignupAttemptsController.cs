using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin view of incomplete signups: people who verified their mobile via
/// OTP but never finished registration. Rows are auto-removed when the
/// signup completes, so this list is the follow-up call sheet.
/// </summary>
[Route("api/v1/admin/signup-attempts")]
public class AdminSignupAttemptsController : AdminControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public AdminSignupAttemptsController(
        ApplicationDbContext dbContext,
        ILogger<AdminSignupAttemptsController> logger)
        : base(logger)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// List incomplete signups, newest verification first.
    /// </summary>
    /// <param name="contacted">null = all, true = already called, false = not yet called</param>
    /// <param name="search">Matches mobile, name, or email</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? contacted = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.SignupAttempts.AsNoTracking();

        if (contacted == true)
        {
            query = query.Where(a => a.ContactedAt != null);
        }
        else if (contacted == false)
        {
            query = query.Where(a => a.ContactedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(a =>
                a.Mobile.Contains(term) ||
                (a.Name != null && a.Name.ToLower().Contains(term)) ||
                (a.Email != null && a.Email.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.MobileVerifiedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new SignupAttemptDto(
                a.Id, a.Mobile, a.Name, a.Email, a.Source,
                a.MobileVerifiedAt, a.ContactedAt, a.ContactNotes, a.CreatedAt))
            .ToListAsync();

        return Success(new SignupAttemptListResponse(items, total, page, pageSize));
    }

    /// <summary>
    /// Mark a lead as contacted (or not) and store call notes.
    /// </summary>
    [HttpPost("{id:guid}/contact")]
    public async Task<IActionResult> SetContacted(Guid id, [FromBody] SignupAttemptContactRequest request)
    {
        var attempt = await _dbContext.SignupAttempts.FirstOrDefaultAsync(a => a.Id == id);
        if (attempt == null)
        {
            return NotFoundResponse("Signup attempt not found");
        }

        attempt.ContactedAt = request.Contacted ? DateTime.UtcNow : null;
        if (request.Notes != null)
        {
            attempt.ContactNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        }
        attempt.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Admin {AdminId} marked signup attempt {AttemptId} contacted={Contacted}",
            CurrentAdminId, id, request.Contacted);

        return Success(new SignupAttemptDto(
            attempt.Id, attempt.Mobile, attempt.Name, attempt.Email, attempt.Source,
            attempt.MobileVerifiedAt, attempt.ContactedAt, attempt.ContactNotes, attempt.CreatedAt));
    }

    /// <summary>
    /// Remove a lead manually (e.g. wrong number, asked not to be called).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _dbContext.SignupAttempts
            .Where(a => a.Id == id)
            .ExecuteDeleteAsync();

        if (deleted == 0)
        {
            return NotFoundResponse("Signup attempt not found");
        }

        _logger.LogInformation("Admin {AdminId} deleted signup attempt {AttemptId}", CurrentAdminId, id);
        return Success(new { deleted = true });
    }
}
