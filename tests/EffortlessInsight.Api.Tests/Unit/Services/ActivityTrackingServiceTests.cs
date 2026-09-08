using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Analytics;
using EffortlessInsight.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class ActivityTrackingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ActivityTrackingService _service;

    private const string VisitorId = "11111111-aaaa-bbbb-cccc-000000000001";
    private const string SessionId = "22222222-aaaa-bbbb-cccc-000000000002";

    public ActivityTrackingServiceTests()
    {
        _dbContext = BillingTestDbContextFactory.Create();
        _service = new ActivityTrackingService(
            _dbContext,
            new Mock<ILogger<ActivityTrackingService>>().Object);
    }

    public void Dispose() => _dbContext.Dispose();

    private static ActivityTrackRequest Batch(params ActivityEventInput[] events) =>
        new(VisitorId, SessionId, events.ToList());

    #region TrackBatchAsync

    [Fact]
    public async Task TrackBatch_ShouldCreateEventsAndVisitorProfile()
    {
        await _service.TrackBatchAsync(Batch(
            new ActivityEventInput("session_start", Page: "/pricing", Referrer: "https://google.com"),
            new ActivityEventInput("page_view", Page: "/pricing")),
            userId: null, userAgent: "TestBrowser/1.0");

        var events = await _dbContext.ActivityEvents.ToListAsync();
        events.Should().HaveCount(2);
        events.Should().OnlyContain(e => e.VisitorId == VisitorId && e.SessionId == SessionId && e.UserId == null);

        var visitor = await _dbContext.Visitors.SingleAsync();
        visitor.VisitorId.Should().Be(VisitorId);
        visitor.LandingPage.Should().Be("/pricing");
        visitor.FirstReferrer.Should().Be("https://google.com");
        visitor.UserAgent.Should().Be("TestBrowser/1.0");
        visitor.UserId.Should().BeNull();
    }

    [Fact]
    public async Task TrackBatch_WithAuthenticatedUser_ShouldLinkVisitorToAccount()
    {
        // Anonymous browsing first
        await _service.TrackBatchAsync(Batch(new ActivityEventInput("page_view", Page: "/")), null, null);

        // Then a login arrives with a user id — the anonymous → authenticated transition
        var userId = Guid.NewGuid();
        await _service.TrackBatchAsync(Batch(new ActivityEventInput("login")), userId, null);

        var visitor = await _dbContext.Visitors.SingleAsync();
        visitor.UserId.Should().Be(userId);
        visitor.LinkedAt.Should().NotBeNull();

        // A different user on the same browser must NOT steal the link
        await _service.TrackBatchAsync(Batch(new ActivityEventInput("login")), Guid.NewGuid(), null);
        (await _dbContext.Visitors.SingleAsync()).UserId.Should().Be(userId);
    }

    [Fact]
    public async Task TrackBatch_WithMalformedIds_ShouldDropSilently()
    {
        await _service.TrackBatchAsync(
            new ActivityTrackRequest("bad id with spaces", SessionId,
                [new ActivityEventInput("page_view")]), null, null);
        await _service.TrackBatchAsync(
            new ActivityTrackRequest(VisitorId, "x",
                [new ActivityEventInput("page_view")]), null, null);

        (await _dbContext.ActivityEvents.CountAsync()).Should().Be(0);
        (await _dbContext.Visitors.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TrackBatch_ShouldSanitizeEventTypesAndStripQueryStrings()
    {
        await _service.TrackBatchAsync(Batch(
            new ActivityEventInput("Page View!", Page: "/notices?token=secret#frag"),
            new ActivityEventInput("///")),  // nothing valid left → skipped
            null, null);

        var events = await _dbContext.ActivityEvents.ToListAsync();
        events.Should().HaveCount(1);
        events[0].EventType.Should().Be("pageview");
        events[0].Page.Should().Be("/notices");
    }

    [Fact]
    public async Task TrackBatch_ShouldCapBatchSize()
    {
        var events = Enumerable.Range(0, 100)
            .Select(_ => new ActivityEventInput("page_view", Page: "/"))
            .ToArray();

        await _service.TrackBatchAsync(Batch(events), null, null);

        (await _dbContext.ActivityEvents.CountAsync()).Should().Be(50);
    }

    [Fact]
    public async Task TrackBatch_WithEmptyOrNullEvents_ShouldNotThrow()
    {
        await _service.TrackBatchAsync(new ActivityTrackRequest(VisitorId, SessionId, []), null, null);
        await _service.TrackBatchAsync(null!, null, null);

        (await _dbContext.ActivityEvents.CountAsync()).Should().Be(0);
    }

    #endregion

    #region Analytics

    private async Task SeedJourneyAsync(Guid userId)
    {
        // Anonymous browsing → signup → authenticated activity
        await _service.TrackBatchAsync(Batch(
            new ActivityEventInput("session_start", Page: "/", Referrer: "https://x.com"),
            new ActivityEventInput("page_view", Page: "/"),
            new ActivityEventInput("page_view", Page: "/pricing"),
            new ActivityEventInput("signup_started")), null, null);

        await _service.TrackBatchAsync(Batch(
            new ActivityEventInput("signup_completed")), userId, null);

        await _service.TrackBatchAsync(Batch(
            new ActivityEventInput("page_view", Page: "/dashboard"),
            new ActivityEventInput("notice_view", EntityType: "notice", EntityId: Guid.NewGuid())),
            userId, null);
    }

    [Fact]
    public async Task Overview_ShouldCountVisitorsSessionsPageViewsAndConversion()
    {
        await SeedJourneyAsync(Guid.NewGuid());

        var overview = await _service.GetOverviewAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        overview.UniqueVisitors.Should().Be(1);
        overview.AuthenticatedVisitors.Should().Be(1);
        overview.AnonymousVisitors.Should().Be(0);
        overview.Sessions.Should().Be(1);
        overview.PageViews.Should().Be(3);
        overview.Signups.Should().Be(1);
        overview.ConversionRate.Should().Be(100);
    }

    [Fact]
    public async Task TopPages_ShouldRankByViews()
    {
        await SeedJourneyAsync(Guid.NewGuid());

        var pages = await _service.GetTopPagesAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), 10);

        pages.Should().HaveCount(3);
        pages.Select(p => p.Key).Should().Contain(["/", "/pricing", "/dashboard"]);
        pages.Should().OnlyContain(p => p.Count == 1 && p.Visitors == 1);
    }

    [Fact]
    public async Task GetEvents_ShouldFilterByTypeAndAudience()
    {
        await SeedJourneyAsync(Guid.NewGuid());
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);

        var signups = await _service.GetEventsAsync(from, to, null, null, "signup_completed", null, null, 1, 50);
        signups.Total.Should().Be(1);

        var anonymous = await _service.GetEventsAsync(from, to, null, null, null, null, false, 1, 50);
        anonymous.Items.Should().OnlyContain(e => e.UserId == null);
        anonymous.Total.Should().Be(4);

        var authenticated = await _service.GetEventsAsync(from, to, null, null, null, null, true, 1, 50);
        authenticated.Total.Should().Be(3);
    }

    [Fact]
    public async Task Journey_ShouldIncludeAnonymousActivityBeforeSignup()
    {
        var userId = Guid.NewGuid();
        await SeedJourneyAsync(userId);

        var journey = await _service.GetJourneyAsync(VisitorId, null, 1, 100);

        journey.Should().NotBeNull();
        journey!.Visitor.UserId.Should().Be(userId);
        journey.Events.Should().HaveCount(7);
        // Chronological: the anonymous events come before the signup
        journey.Events.First().EventType.Should().Be("session_start");
        journey.Events.Select(e => e.EventType).Should().ContainInOrder(
            "signup_started", "signup_completed", "notice_view");

        // Same journey must be reachable by user id
        var byUser = await _service.GetJourneyAsync(null, userId, 1, 100);
        byUser!.Events.Should().HaveCount(7);
    }

    #endregion
}
