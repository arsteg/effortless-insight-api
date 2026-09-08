using System.Text.RegularExpressions;
using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Analytics;

public interface IActivityTrackingService
{
    /// <summary>
    /// Records a batch of client events. Never throws — tracking failures
    /// must not break the caller's workflow.
    /// </summary>
    Task TrackBatchAsync(ActivityTrackRequest request, Guid? userId, string? userAgent);

    Task<ActivityOverviewResponse> GetOverviewAsync(DateTime from, DateTime to);
    Task<List<ActivityTrendPoint>> GetTrendsAsync(DateTime from, DateTime to);
    Task<List<ActivityCountItem>> GetTopPagesAsync(DateTime from, DateTime to, int limit);
    Task<List<ActivityCountItem>> GetTopEventsAsync(DateTime from, DateTime to, int limit);
    Task<ActivityEventListResponse> GetEventsAsync(
        DateTime from, DateTime to, string? visitorId, Guid? userId, string? eventType,
        string? page, bool? authenticated, int pageNumber, int pageSize);
    Task<VisitorListResponse> GetVisitorsAsync(
        DateTime from, DateTime to, bool? authenticated, string? search, int pageNumber, int pageSize);
    Task<VisitorJourneyResponse?> GetJourneyAsync(string? visitorId, Guid? userId, int pageNumber, int pageSize);
}

public partial class ActivityTrackingService : IActivityTrackingService
{
    private const int MaxEventsPerBatch = 50;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ActivityTrackingService> _logger;

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("[^a-z0-9_.]")]
    private static partial Regex TypeCleanup();

    public ActivityTrackingService(
        ApplicationDbContext dbContext,
        ILogger<ActivityTrackingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Ingestion
    // ------------------------------------------------------------------

    public async Task TrackBatchAsync(ActivityTrackRequest request, Guid? userId, string? userAgent)
    {
        try
        {
            if (request?.Events == null || request.Events.Count == 0)
            {
                return;
            }

            if (!IdPattern().IsMatch(request.VisitorId ?? string.Empty) ||
                !IdPattern().IsMatch(request.SessionId ?? string.Empty))
            {
                return; // malformed client — drop silently
            }

            var now = DateTime.UtcNow;
            var events = new List<ActivityEvent>();

            foreach (var input in request.Events.Take(MaxEventsPerBatch))
            {
                var type = SanitizeType(input.Type);
                if (type == null)
                {
                    continue;
                }

                events.Add(new ActivityEvent
                {
                    VisitorId = request.VisitorId!,
                    UserId = userId,
                    SessionId = request.SessionId!,
                    EventType = type,
                    EventName = Truncate(input.Name, 150),
                    Page = Truncate(StripQuery(input.Page), 300),
                    Referrer = Truncate(input.Referrer, 500),
                    EntityType = Truncate(input.EntityType, 50),
                    EntityId = input.EntityId,
                    MetadataJson = Truncate(input.Metadata, 2000),
                    CreatedAt = now
                });
            }

            if (events.Count == 0)
            {
                return;
            }

            await UpsertVisitorAsync(request.VisitorId!, userId, userAgent, events, now);
            _dbContext.ActivityEvents.AddRange(events);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Tracking must never break anything — log and move on.
            _logger.LogWarning(ex, "Activity tracking batch failed");
        }
    }

    private async Task UpsertVisitorAsync(
        string visitorId, Guid? userId, string? userAgent, List<ActivityEvent> events, DateTime now)
    {
        var visitor = await _dbContext.Visitors.FirstOrDefaultAsync(v => v.VisitorId == visitorId);
        if (visitor == null)
        {
            var firstNavigation = events.FirstOrDefault(e =>
                e.EventType == ActivityEventTypes.SessionStart ||
                e.EventType == ActivityEventTypes.PageView);

            visitor = new Visitor
            {
                VisitorId = visitorId,
                FirstSeenAt = now,
                FirstReferrer = firstNavigation?.Referrer,
                LandingPage = firstNavigation?.Page,
                UserAgent = Truncate(userAgent, 300)
            };
            _dbContext.Visitors.Add(visitor);
        }

        visitor.LastSeenAt = now;
        if (userId != null && visitor.UserId == null)
        {
            // Anonymous → authenticated: link the pre-signup journey to the account
            visitor.UserId = userId;
            visitor.LinkedAt = now;
        }
        if (visitor.Id != Guid.Empty && _dbContext.Entry(visitor).State == EntityState.Modified)
        {
            visitor.UpdatedAt = now;
        }
    }

    private static string? SanitizeType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var cleaned = TypeCleanup().Replace(type.Trim().ToLowerInvariant(), "");
        if (cleaned.Length == 0)
        {
            return null;
        }
        return cleaned.Length <= 50 ? cleaned : cleaned[..50];
    }

    /// <summary>Query strings may carry tokens or PII — keep the path only.</summary>
    private static string? StripQuery(string? page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return null;
        }
        var cut = page.IndexOfAny(['?', '#']);
        return cut >= 0 ? page[..cut] : page;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    // ------------------------------------------------------------------
    // Analytics (admin)
    // ------------------------------------------------------------------

    private IQueryable<ActivityEvent> InRange(DateTime from, DateTime to) =>
        _dbContext.ActivityEvents.AsNoTracking()
            .Where(e => e.CreatedAt >= from && e.CreatedAt < to);

    public async Task<ActivityOverviewResponse> GetOverviewAsync(DateTime from, DateTime to)
    {
        var range = InRange(from, to);

        var uniqueVisitors = await range.Select(e => e.VisitorId).Distinct().CountAsync();
        var authenticatedVisitors = await range
            .Where(e => e.UserId != null)
            .Select(e => e.VisitorId).Distinct().CountAsync();
        var sessions = await range.Select(e => e.SessionId).Distinct().CountAsync();
        var pageViews = await range.CountAsync(e => e.EventType == ActivityEventTypes.PageView);
        var totalEvents = await range.CountAsync();
        var signups = await range.CountAsync(e => e.EventType == ActivityEventTypes.SignupCompleted);
        var logins = await range.CountAsync(e => e.EventType == ActivityEventTypes.Login);

        return new ActivityOverviewResponse(
            UniqueVisitors: uniqueVisitors,
            AnonymousVisitors: uniqueVisitors - authenticatedVisitors,
            AuthenticatedVisitors: authenticatedVisitors,
            Sessions: sessions,
            PageViews: pageViews,
            TotalEvents: totalEvents,
            Signups: signups,
            Logins: logins,
            ConversionRate: uniqueVisitors == 0 ? 0 : Math.Round((double)signups / uniqueVisitors * 100, 2));
    }

    public async Task<List<ActivityTrendPoint>> GetTrendsAsync(DateTime from, DateTime to)
    {
        var raw = await InRange(from, to)
            .GroupBy(e => new { e.CreatedAt.Year, e.CreatedAt.Month, e.CreatedAt.Day })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                g.Key.Day,
                Visitors = g.Select(e => e.VisitorId).Distinct().Count(),
                PageViews = g.Count(e => e.EventType == ActivityEventTypes.PageView),
                Events = g.Count(),
                Signups = g.Count(e => e.EventType == ActivityEventTypes.SignupCompleted)
            })
            .ToListAsync();

        // Fill missing days with zeros so charts have a continuous axis
        var byDate = raw.ToDictionary(
            x => new DateTime(x.Year, x.Month, x.Day, 0, 0, 0, DateTimeKind.Utc),
            x => x);
        var points = new List<ActivityTrendPoint>();
        for (var day = from.Date; day < to; day = day.AddDays(1))
        {
            var key = DateTime.SpecifyKind(day, DateTimeKind.Utc);
            points.Add(byDate.TryGetValue(key, out var x)
                ? new ActivityTrendPoint(key, x.Visitors, x.PageViews, x.Events, x.Signups)
                : new ActivityTrendPoint(key, 0, 0, 0, 0));
        }
        return points;
    }

    public async Task<List<ActivityCountItem>> GetTopPagesAsync(DateTime from, DateTime to, int limit)
    {
        var items = await InRange(from, to)
            .Where(e => e.EventType == ActivityEventTypes.PageView && e.Page != null)
            .GroupBy(e => e.Page!)
            .Select(g => new
            {
                Key = g.Key,
                Count = g.Count(),
                Visitors = g.Select(e => e.VisitorId).Distinct().Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync();

        return items.Select(x => new ActivityCountItem(x.Key, x.Count, x.Visitors)).ToList();
    }

    public async Task<List<ActivityCountItem>> GetTopEventsAsync(DateTime from, DateTime to, int limit)
    {
        var items = await InRange(from, to)
            .Where(e => e.EventType != ActivityEventTypes.PageView)
            .GroupBy(e => e.EventType)
            .Select(g => new
            {
                Key = g.Key,
                Count = g.Count(),
                Visitors = g.Select(e => e.VisitorId).Distinct().Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(limit)
            .ToListAsync();

        return items.Select(x => new ActivityCountItem(x.Key, x.Count, x.Visitors)).ToList();
    }

    public async Task<ActivityEventListResponse> GetEventsAsync(
        DateTime from, DateTime to, string? visitorId, Guid? userId, string? eventType,
        string? page, bool? authenticated, int pageNumber, int pageSize)
    {
        var query = InRange(from, to);

        if (!string.IsNullOrWhiteSpace(visitorId))
        {
            query = query.Where(e => e.VisitorId == visitorId);
        }
        if (userId != null)
        {
            query = query.Where(e => e.UserId == userId);
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }
        if (!string.IsNullOrWhiteSpace(page))
        {
            query = query.Where(e => e.Page != null && e.Page.Contains(page));
        }
        if (authenticated == true)
        {
            query = query.Where(e => e.UserId != null);
        }
        else if (authenticated == false)
        {
            query = query.Where(e => e.UserId == null);
        }

        var total = await query.CountAsync();
        var items = await ProjectWithUser(query.OrderByDescending(e => e.CreatedAt))
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ActivityEventListResponse(items, total, pageNumber, pageSize);
    }

    public async Task<VisitorListResponse> GetVisitorsAsync(
        DateTime from, DateTime to, bool? authenticated, string? search, int pageNumber, int pageSize)
    {
        var query = _dbContext.Visitors.AsNoTracking()
            .Where(v => v.LastSeenAt >= from && v.FirstSeenAt < to);

        if (authenticated == true)
        {
            query = query.Where(v => v.UserId != null);
        }
        else if (authenticated == false)
        {
            query = query.Where(v => v.UserId == null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(v =>
                v.VisitorId.ToLower().Contains(term) ||
                _dbContext.Users.Any(u =>
                    u.Id == v.UserId &&
                    (u.Email!.ToLower().Contains(term) || u.Name.ToLower().Contains(term))));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.LastSeenAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new VisitorListItem(
                v.VisitorId,
                v.UserId,
                _dbContext.Users.Where(u => u.Id == v.UserId).Select(u => u.Name).FirstOrDefault(),
                _dbContext.Users.Where(u => u.Id == v.UserId).Select(u => u.Email).FirstOrDefault(),
                v.FirstSeenAt,
                v.LastSeenAt,
                v.FirstReferrer,
                v.LandingPage,
                _dbContext.ActivityEvents.Count(e => e.VisitorId == v.VisitorId)))
            .ToListAsync();

        return new VisitorListResponse(items, total, pageNumber, pageSize);
    }

    public async Task<VisitorJourneyResponse?> GetJourneyAsync(
        string? visitorId, Guid? userId, int pageNumber, int pageSize)
    {
        Visitor? visitor = null;
        if (!string.IsNullOrWhiteSpace(visitorId))
        {
            visitor = await _dbContext.Visitors.AsNoTracking()
                .FirstOrDefaultAsync(v => v.VisitorId == visitorId);
        }
        else if (userId != null)
        {
            visitor = await _dbContext.Visitors.AsNoTracking()
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.LastSeenAt)
                .FirstOrDefaultAsync();
        }

        var effectiveUserId = userId ?? visitor?.UserId;
        if (visitor == null && effectiveUserId == null)
        {
            return null;
        }

        // The complete journey: everything from this browser (visitorId,
        // including pre-signup anonymous events) plus everything from the
        // linked account (covers other devices after login).
        var vid = visitor?.VisitorId;
        var query = _dbContext.ActivityEvents.AsNoTracking()
            .Where(e =>
                (vid != null && e.VisitorId == vid) ||
                (effectiveUserId != null && e.UserId == effectiveUserId));

        var total = await query.CountAsync();
        var events = await ProjectWithUser(query.OrderBy(e => e.CreatedAt))
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var profile = new VisitorListItem(
            visitor?.VisitorId ?? "-",
            effectiveUserId,
            effectiveUserId == null
                ? null
                : await _dbContext.Users.Where(u => u.Id == effectiveUserId).Select(u => u.Name).FirstOrDefaultAsync(),
            effectiveUserId == null
                ? null
                : await _dbContext.Users.Where(u => u.Id == effectiveUserId).Select(u => u.Email).FirstOrDefaultAsync(),
            visitor?.FirstSeenAt ?? DateTime.MinValue,
            visitor?.LastSeenAt ?? DateTime.MinValue,
            visitor?.FirstReferrer,
            visitor?.LandingPage,
            total);

        return new VisitorJourneyResponse(profile, events, total, pageNumber, pageSize);
    }

    private IQueryable<ActivityEventDto> ProjectWithUser(IQueryable<ActivityEvent> query) =>
        query.Select(e => new ActivityEventDto(
            e.Id,
            e.VisitorId,
            e.UserId,
            _dbContext.Users.Where(u => u.Id == e.UserId).Select(u => u.Name).FirstOrDefault(),
            _dbContext.Users.Where(u => u.Id == e.UserId).Select(u => u.Email).FirstOrDefault(),
            e.SessionId,
            e.EventType,
            e.EventName,
            e.Page,
            e.Referrer,
            e.EntityType,
            e.EntityId,
            e.MetadataJson,
            e.CreatedAt));
}
