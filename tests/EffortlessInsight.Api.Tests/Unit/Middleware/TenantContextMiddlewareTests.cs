using EffortlessInsight.Api.Middleware;
using EffortlessInsight.Api.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Middleware;

/// <summary>
/// Phase 5 hardening: the unsigned X-Organization-Id header must never be
/// honored in production, since (unlike the signed org_id JWT claim) it is
/// trivially spoofable by the caller. Confirmed exploitable pre-fix via
/// NoticeConversationsController's AI chat services, which scope conversations/
/// notices purely by ITenantContext.OrganizationId with no separate membership
/// check - see ContextRetrievalService/ConversationService.
/// </summary>
public class TenantContextMiddlewareTests
{
    private static (TenantContextMiddleware Middleware, TenantContext TenantContext) CreateMiddleware(string environmentName)
    {
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns(environmentName);

        var tenantContext = new TenantContext();
        RequestDelegate next = _ => Task.CompletedTask;
        var middleware = new TenantContextMiddleware(next, NullLogger<TenantContextMiddleware>.Instance, envMock.Object);

        return (middleware, tenantContext);
    }

    private static HttpContext CreateHttpContextWithHeader(string orgId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Organization-Id"] = orgId;
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ProductionEnvironment_IgnoresHeaderFallback()
    {
        var (middleware, tenantContext) = CreateMiddleware("Production");
        var orgId = Guid.NewGuid();
        var context = CreateHttpContextWithHeader(orgId.ToString());

        await middleware.InvokeAsync(context, tenantContext);

        tenantContext.OrganizationId.Should().BeNull(
            "the unsigned header must never set tenant context in production");
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentEnvironment_HonorsHeaderFallback()
    {
        var (middleware, tenantContext) = CreateMiddleware("Development");
        var orgId = Guid.NewGuid();
        var context = CreateHttpContextWithHeader(orgId.ToString());

        await middleware.InvokeAsync(context, tenantContext);

        tenantContext.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task InvokeAsync_LocalEnvironment_HonorsHeaderFallback()
    {
        var (middleware, tenantContext) = CreateMiddleware("Local");
        var orgId = Guid.NewGuid();
        var context = CreateHttpContextWithHeader(orgId.ToString());

        await middleware.InvokeAsync(context, tenantContext);

        tenantContext.OrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task InvokeAsync_SignedClaimPresent_TakesPrecedenceOverHeaderInAnyEnvironment()
    {
        var (middleware, tenantContext) = CreateMiddleware("Development");
        var claimOrgId = Guid.NewGuid();
        var headerOrgId = Guid.NewGuid();

        var context = CreateHttpContextWithHeader(headerOrgId.ToString());
        var claims = new List<System.Security.Claims.Claim> { new("org_id", claimOrgId.ToString()) };
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, "TestAuth"));

        await middleware.InvokeAsync(context, tenantContext);

        tenantContext.OrganizationId.Should().Be(claimOrgId);
    }
}
