using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Collaboration;

/// <summary>
/// Interface for time tracking operations on tasks.
/// </summary>
public interface ITimeTrackingService
{
    /// <summary>
    /// Log time manually for a task.
    /// </summary>
    Task<TimeEntry> LogTimeAsync(Guid taskId, Guid userId, decimal hours, DateOnly date, string? description, bool isBillable, CancellationToken ct);

    /// <summary>
    /// Start a timer for a task.
    /// </summary>
    Task<TimeEntry> StartTimerAsync(Guid taskId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Stop a running timer.
    /// </summary>
    Task<TimeEntry> StopTimerAsync(Guid entryId, CancellationToken ct);

    /// <summary>
    /// Get all time entries for a task.
    /// </summary>
    Task<List<TimeEntry>> GetTimeEntriesAsync(Guid taskId, CancellationToken ct);

    /// <summary>
    /// Get total hours logged for a task.
    /// </summary>
    Task<decimal> GetTotalHoursAsync(Guid taskId, CancellationToken ct);

    /// <summary>
    /// Delete a time entry.
    /// </summary>
    Task DeleteTimeEntryAsync(Guid entryId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Update an existing time entry.
    /// </summary>
    Task<TimeEntry> UpdateTimeEntryAsync(Guid entryId, Guid userId, UpdateTimeEntryDto dto, CancellationToken ct);

    /// <summary>
    /// Get active timer for a user on a task (if any).
    /// </summary>
    Task<TimeEntry?> GetActiveTimerAsync(Guid taskId, Guid userId, CancellationToken ct);
}

/// <summary>
/// Implementation of time tracking service.
/// </summary>
public class TimeTrackingService : ITimeTrackingService
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityService _activityService;
    private readonly ILogger<TimeTrackingService> _logger;

    public TimeTrackingService(
        ApplicationDbContext context,
        IActivityService activityService,
        ILogger<TimeTrackingService> logger)
    {
        _context = context;
        _activityService = activityService;
        _logger = logger;
    }

    public async Task<TimeEntry> LogTimeAsync(
        Guid taskId,
        Guid userId,
        decimal hours,
        DateOnly date,
        string? description,
        bool isBillable,
        CancellationToken ct)
    {
        if (hours <= 0 || hours > 24)
        {
            throw new InvalidOperationException("Hours must be between 0 and 24");
        }

        var task = await GetTaskWithAccessCheckAsync(taskId, userId, ct);

        var entry = new TimeEntry
        {
            TaskId = taskId,
            UserId = userId,
            Date = date,
            Hours = hours,
            Description = description,
            IsBillable = isBillable
        };

        _context.TimeEntries.Add(entry);

        // Update task's actual hours
        await UpdateTaskActualHoursAsync(taskId, ct);

        await _context.SaveChangesAsync(ct);

        // Log activity
        await _activityService.LogActivityAsync(
            task.Notice.OrganizationId,
            task.NoticeId,
            ActivityTypes.TimeLogged,
            userId,
            new Dictionary<string, object>
            {
                ["taskId"] = taskId,
                ["taskTitle"] = task.Title,
                ["hours"] = hours,
                ["date"] = date.ToString("yyyy-MM-dd")
            },
            $"logged {hours:0.##}h on task \"{task.Title}\""
        );

        _logger.LogInformation(
            "Time entry {EntryId} created: {Hours}h for task {TaskId} by user {UserId}",
            entry.Id, hours, taskId, userId);

        return entry;
    }

    public async Task<TimeEntry> StartTimerAsync(Guid taskId, Guid userId, CancellationToken ct)
    {
        var task = await GetTaskWithAccessCheckAsync(taskId, userId, ct);

        // Check if user already has a running timer on this task
        var existingTimer = await _context.TimeEntries
            .FirstOrDefaultAsync(e =>
                e.TaskId == taskId &&
                e.UserId == userId &&
                e.StartTime != null &&
                e.EndTime == null, ct);

        if (existingTimer != null)
        {
            throw new InvalidOperationException("Timer is already running for this task");
        }

        var entry = new TimeEntry
        {
            TaskId = taskId,
            UserId = userId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Hours = 0,
            StartTime = DateTime.UtcNow,
            IsBillable = true
        };

        _context.TimeEntries.Add(entry);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Timer started: entry {EntryId} for task {TaskId} by user {UserId}",
            entry.Id, taskId, userId);

        return entry;
    }

    public async Task<TimeEntry> StopTimerAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await _context.TimeEntries
            .Include(e => e.Task)
                .ThenInclude(t => t.Notice)
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new KeyNotFoundException("Time entry not found");

        if (!entry.StartTime.HasValue)
        {
            throw new InvalidOperationException("This entry is not a timer entry");
        }

        if (entry.EndTime.HasValue)
        {
            throw new InvalidOperationException("Timer is already stopped");
        }

        entry.EndTime = DateTime.UtcNow;
        entry.Hours = CalculateHours(entry.StartTime.Value, entry.EndTime.Value);
        entry.UpdatedAt = DateTime.UtcNow;

        // Update task's actual hours
        await UpdateTaskActualHoursAsync(entry.TaskId, ct);

        await _context.SaveChangesAsync(ct);

        // Log activity
        await _activityService.LogActivityAsync(
            entry.Task.Notice.OrganizationId,
            entry.Task.NoticeId,
            ActivityTypes.TimeLogged,
            entry.UserId,
            new Dictionary<string, object>
            {
                ["taskId"] = entry.TaskId,
                ["taskTitle"] = entry.Task.Title,
                ["hours"] = entry.Hours,
                ["duration"] = FormatDuration(entry.Hours)
            },
            $"logged {entry.Hours:0.##}h on task \"{entry.Task.Title}\" via timer"
        );

        _logger.LogInformation(
            "Timer stopped: entry {EntryId}, {Hours}h logged",
            entry.Id, entry.Hours);

        return entry;
    }

    public async Task<List<TimeEntry>> GetTimeEntriesAsync(Guid taskId, CancellationToken ct)
    {
        return await _context.TimeEntries
            .Include(e => e.User)
            .Where(e => e.TaskId == taskId)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<decimal> GetTotalHoursAsync(Guid taskId, CancellationToken ct)
    {
        return await _context.TimeEntries
            .Where(e => e.TaskId == taskId)
            .SumAsync(e => e.Hours, ct);
    }

    public async Task DeleteTimeEntryAsync(Guid entryId, Guid userId, CancellationToken ct)
    {
        var entry = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new KeyNotFoundException("Time entry not found");

        // Only the user who created the entry or an admin can delete it
        if (entry.UserId != userId)
        {
            throw new UnauthorizedAccessException("You can only delete your own time entries");
        }

        entry.DeletedAt = DateTime.UtcNow;

        // Update task's actual hours
        await UpdateTaskActualHoursAsync(entry.TaskId, ct);

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Time entry {EntryId} deleted by user {UserId}",
            entryId, userId);
    }

    public async Task<TimeEntry> UpdateTimeEntryAsync(
        Guid entryId,
        Guid userId,
        UpdateTimeEntryDto dto,
        CancellationToken ct)
    {
        var entry = await _context.TimeEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new KeyNotFoundException("Time entry not found");

        // Only the user who created the entry can update it
        if (entry.UserId != userId)
        {
            throw new UnauthorizedAccessException("You can only update your own time entries");
        }

        // Cannot update a running timer entry
        if (entry.IsTimerRunning)
        {
            throw new InvalidOperationException("Cannot update a running timer. Stop the timer first.");
        }

        if (dto.Hours.HasValue)
        {
            if (dto.Hours.Value <= 0 || dto.Hours.Value > 24)
            {
                throw new InvalidOperationException("Hours must be between 0 and 24");
            }
            entry.Hours = dto.Hours.Value;
        }

        if (dto.Date.HasValue)
        {
            entry.Date = dto.Date.Value;
        }

        if (dto.Description != null)
        {
            entry.Description = dto.Description;
        }

        if (dto.IsBillable.HasValue)
        {
            entry.IsBillable = dto.IsBillable.Value;
        }

        entry.UpdatedAt = DateTime.UtcNow;

        // Update task's actual hours
        await UpdateTaskActualHoursAsync(entry.TaskId, ct);

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Time entry {EntryId} updated by user {UserId}",
            entryId, userId);

        return entry;
    }

    public async Task<TimeEntry?> GetActiveTimerAsync(Guid taskId, Guid userId, CancellationToken ct)
    {
        return await _context.TimeEntries
            .FirstOrDefaultAsync(e =>
                e.TaskId == taskId &&
                e.UserId == userId &&
                e.StartTime != null &&
                e.EndTime == null, ct);
    }

    // Private helper methods

    private async Task<NoticeTask> GetTaskWithAccessCheckAsync(Guid taskId, Guid userId, CancellationToken ct)
    {
        var task = await _context.Tasks
            .Include(t => t.Notice)
            .FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new KeyNotFoundException("Task not found");

        // Verify user has access to this task's organization
        var isMember = await _context.OrganizationMembers
            .AnyAsync(m => m.OrganizationId == task.Notice.OrganizationId && m.UserId == userId, ct);

        if (!isMember)
        {
            throw new UnauthorizedAccessException("You do not have access to this task");
        }

        return task;
    }

    private async Task UpdateTaskActualHoursAsync(Guid taskId, CancellationToken ct)
    {
        var totalHours = await _context.TimeEntries
            .Where(e => e.TaskId == taskId && !e.IsTimerRunning)
            .SumAsync(e => e.Hours, ct);

        var task = await _context.Tasks.FindAsync(new object[] { taskId }, ct);
        if (task != null)
        {
            task.ActualHours = totalHours;
            task.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static decimal CalculateHours(DateTime start, DateTime end)
    {
        var duration = end - start;
        // Round to 2 decimal places, minimum 0.01 hours (about 36 seconds)
        var hours = Math.Max(0.01m, Math.Round((decimal)duration.TotalHours, 2));
        return hours;
    }

    private static string FormatDuration(decimal hours)
    {
        var ts = TimeSpan.FromHours((double)hours);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }
        return $"{ts.Minutes}m";
    }
}

