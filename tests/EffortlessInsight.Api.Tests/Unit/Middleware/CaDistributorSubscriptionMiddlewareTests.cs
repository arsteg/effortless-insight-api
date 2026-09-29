using System.Security.Claims;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Middleware;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Middleware;

public class CaDistributorSubscriptionMiddlewareTests
{
    [Fact]
    public async Task RecipientOnboardingCanRunWithoutSelectedOrganization()
    {
        var called = false;
        var middleware = new SubscriptionEnforcementMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<SubscriptionEnforcementMiddleware>.Instance);
        var context = Authenticated(Guid.NewGuid());
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new CaInvitationOnboardingAttribute()), "prepare"));
        await using var db = BillingTestDbContextFactory.Create();
        await middleware.InvokeAsync(context, new Mock<ICurrentOrganizationService>().Object, db);
        called.Should().BeTrue();
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData("ca", false)]
    public async Task FreeCaGrantOnlyBypassesSubscriptionInOwnOrganization(string role, bool expected)
    {
        var userId = Guid.NewGuid();
        var organization = new Organization { Name = "Org", NameNormalized = "org", State = "Maharashtra", SubscriptionStatus = "none" };
        await using var db = BillingTestDbContextFactory.Create();
        db.Organizations.Add(organization);
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = organization.Id, UserId = userId, Role = role, Status = "active" });
        await db.SaveChangesAsync();
        var current = new Mock<ICurrentOrganizationService>();
        current.SetupGet(c => c.OrganizationId).Returns(organization.Id);
        var grant = new Mock<ICaAccessService>();
        grant.Setup(g => g.HasActiveFreeAccessAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var services = new ServiceCollection().AddSingleton(grant.Object).BuildServiceProvider();
        var context = Authenticated(userId);
        context.RequestServices = services;
        var called = false;
        var middleware = new SubscriptionEnforcementMiddleware(_ => { called = true; return Task.CompletedTask; },
            NullLogger<SubscriptionEnforcementMiddleware>.Instance);
        await middleware.InvokeAsync(context, current.Object, db);
        called.Should().Be(expected);
        if (!expected) context.Response.StatusCode.Should().Be(402);
    }

    private static DefaultHttpContext Authenticated(Guid userId)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test")) };
        context.Request.Path = "/api/v1/notices";
        context.Response.Body = new MemoryStream();
        return context;
    }
}
