using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services;
using EffortlessInsight.Api.Services.Billing;
using EffortlessInsight.Api.Services.Email;
using EffortlessInsight.Api.Services.Encryption;
using EffortlessInsight.Api.Services.GstSync;
using EffortlessInsight.Api.Services.Organizations;
using EffortlessInsight.Api.Tests.Helpers;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EffortlessInsight.Api.Tests.Unit.Services;

/// <summary>
/// Covers the Phase 3 handoff: BO accepts a CaClientInvitation, a real
/// organization is created, the CA is added as a role="ca" member, and staged
/// notices are merged into it with zero duplicates.
///
/// IOrganizationManagementService is mocked (its own CreateAsync behavior -
/// GSTIN validation, plan limits, etc. - is covered by OrganizationService's own
/// integration tests). The mock still performs a real, minimal org/membership/
/// GSTIN insert against the test database, since CaClientService's merge logic
/// reads those rows back - the goal here is to verify CaClientService's own
/// orchestration and merge algorithm, not re-test org creation.
/// </summary>
public class CaClientServiceAcceptInvitationTests
{
    static CaClientServiceAcceptInvitationTests()
    {
        if (!FieldEncryptionServiceAccessor.IsConfigured)
        {
            FieldEncryptionServiceAccessor.SetInstance(
                new FieldEncryptionService(new ConfigurationBuilder().Build(), NullLogger<FieldEncryptionService>.Instance));
        }
    }

    private const string TestGstin = "27AABCU9603R1ZN";

    private static (CaClientService Service, ApplicationDbContext Db, Guid CaUserId, Guid BoUserId, Guid OrgIdToCreate)
        CreateService(Mock<IOrganizationManagementService>? orgServiceMock = null)
    {
        var db = BillingTestDbContextFactory.Create();
        var caUserId = Guid.NewGuid();
        var boUserId = Guid.NewGuid();
        var orgIdToCreate = Guid.NewGuid();

        var caUser = new ApplicationUser { Id = caUserId, Email = "ca@example.com", UserName = "ca@example.com", NormalizedEmail = "CA@EXAMPLE.COM", Name = "Test CA", IsCA = true };
        var boUser = new ApplicationUser { Id = boUserId, Email = "bo@example.com", UserName = "bo@example.com", NormalizedEmail = "BO@EXAMPLE.COM", Name = "Test BO" };
        db.Users.AddRange(caUser, boUser);
        db.SaveChanges();

        var userManagerMock = MockHelpers.CreateMockUserManager();
        userManagerMock.Setup(m => m.FindByIdAsync(caUserId.ToString())).ReturnsAsync(caUser);
        userManagerMock.Setup(m => m.FindByIdAsync(boUserId.ToString())).ReturnsAsync(boUser);

        var gstinValidator = new GstinValidatorService(db);
        var caGstinAuth = new CaGstinAuthorizationService(db);

        var orgMock = orgServiceMock ?? new Mock<IOrganizationManagementService>();
        orgMock.Setup(s => s.CreateAsync(It.IsAny<CreateOrganizationRequest>(), boUserId))
            .Returns((CreateOrganizationRequest req, Guid userId) =>
            {
                var org = new Organization
                {
                    Id = orgIdToCreate,
                    Name = req.Name,
                    NameNormalized = req.Name.Trim().ToLowerInvariant(),
                    State = req.State,
                    SubscriptionStatus = "none"
                };
                db.Organizations.Add(org);
                db.OrganizationGstins.Add(new OrganizationGstin
                {
                    OrganizationId = org.Id,
                    Gstin = req.Gstin!,
                    StateCode = "27",
                    StateName = "Maharashtra",
                    IsPrimary = true,
                    Status = "active"
                });
                db.OrganizationMembers.Add(new OrganizationMember
                {
                    OrganizationId = org.Id,
                    UserId = userId,
                    Role = "owner",
                    Status = "active"
                });
                db.SaveChanges();

                return Task.FromResult(new CreateOrganizationResponse(
                    Id: org.Id,
                    Name: org.Name,
                    LegalName: req.LegalName,
                    Gstins: [],
                    Industry: req.Industry,
                    State: req.State,
                    City: req.City,
                    SubscriptionStatus: "none",
                    TrialEndsAt: null,
                    MemberCount: 1,
                    CurrentUserRole: "owner",
                    CreatedAt: DateTime.UtcNow,
                    AccessToken: "fake-access-token",
                    ExpiresIn: 900
                ));
            });

        var emailServiceMock = new Mock<IEmailService>();
        emailServiceMock
            .Setup(e => e.SendTemplateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailSendResult("fake-message-id"));

        var fileStorageMock = new Mock<IFileStorageService>();
        var usageServiceMock = new Mock<IUsageService>();
        var backgroundJobsMock = new Mock<IBackgroundJobClient>();
        var auditServiceMock = new Mock<IAuditService>();
        var caBoGstinLinkServiceMock = new Mock<ICaBoGstinLinkService>();
        var gstNoticeRawServiceMock = new Mock<IGstNoticeRawService>();
        gstNoticeRawServiceMock
            .Setup(s => s.AutoImportForGstinAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoImportResult { Imported = 0, SkippedAsDuplicate = 0, Failed = 0 });
        var configuration = new ConfigurationBuilder().Build();

        var service = new CaClientService(
            db,
            gstinValidator,
            caGstinAuth,
            orgMock.Object,
            caBoGstinLinkServiceMock.Object,
            gstNoticeRawServiceMock.Object,
            userManagerMock.Object,
            emailServiceMock.Object,
            fileStorageMock.Object,
            usageServiceMock.Object,
            backgroundJobsMock.Object,
            auditServiceMock.Object,
            configuration,
            NullLogger<CaClientService>.Instance);

        return (service, db, caUserId, boUserId, orgIdToCreate);
    }

    private static (CaClientInvitation Invitation, CaProspectClient ProspectClient) SeedPendingInvitation(
        ApplicationDbContext db, Guid caUserId, string tokenHash, string boEmail = "bo@example.com", int? accessDurationDays = null)
    {
        var caOrg = new Organization
        {
            Name = "CA Firm " + Guid.NewGuid(),
            NameNormalized = "ca firm " + Guid.NewGuid(),
            State = "Maharashtra",
            SubscriptionStatus = "none"
        };
        db.Organizations.Add(caOrg);
        db.SaveChanges();

        var prospectClient = new CaProspectClient
        {
            CaUserId = caUserId,
            Gstin = TestGstin,
            GstinHash = new CaGstinAuthorizationService(db).ComputeGstinHash(TestGstin),
            ClientDisplayName = "ABC Traders",
            Status = "staging"
        };
        db.CaProspectClients.Add(prospectClient);

        var invitation = new CaClientInvitation
        {
            CaUserId = caUserId,
            CaOrganizationId = caOrg.Id,
            Gstin = TestGstin,
            Email = boEmail,
            EmailNormalized = boEmail.ToUpperInvariant(),
            TokenHash = tokenHash,
            Status = "pending",
            ExpiresAt = DateTime.UtcNow.AddDays(14),
            AccessDurationDays = accessDurationDays
        };
        db.OrganizationMembers.Add(new OrganizationMember {
            OrganizationId = caOrg.Id, UserId = caUserId, Role = "owner", Status = "active"
        });
        db.CaClientInvitations.Add(invitation);
        db.SaveChanges();

        prospectClient.CaClientInvitationId = invitation.Id;
        db.SaveChanges();

        return (invitation, prospectClient);
    }

    private static async Task PreparePaidOrganization(CaClientService service, ApplicationDbContext db, string token, Guid boUserId)
    {
        var org = await service.PrepareOrganizationAsync(token, boUserId, DefaultAcceptRequest());
        db.BillingSubscriptions.Add(new EffortlessInsight.Api.Data.Entities.Billing.BillingSubscription {
            OrganizationId = org.OrganizationId, Status = "active", CurrentPeriodEnd = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
    }

    private static AcceptCaClientInvitationRequest DefaultAcceptRequest() => new(
        OrganizationName: "ABC Traders Pvt Ltd",
        LegalName: null,
        Industry: null,
        State: "Maharashtra",
        City: null,
        AnnualTurnoverRange: null
    );

    [Fact]
    public async Task PreparationDoesNotAcceptOrGrantCaAccess()
    {
        var (service, db, ca, bo, _) = CreateService();
        var (invite, prospect) = SeedPendingInvitation(db, ca, HashTokenForTest("prepare"));
        var destination = await service.PrepareOrganizationAsync("prepare", bo, DefaultAcceptRequest());
        invite.Status.Should().Be("pending");
        prospect.Status.Should().Be("staging");
        db.OrganizationMembers.Should().NotContain(m => m.OrganizationId == destination.OrganizationId && m.UserId == ca);
    }

    [Fact]
    public async Task AcceptanceRequiresDestinationSubscription_NotAnotherOrganizationsPlan()
    {
        var (service, db, ca, bo, _) = CreateService();
        var (invite, prospect) = SeedPendingInvitation(db, ca, HashTokenForTest("unpaid"));
        var destination = await service.PrepareOrganizationAsync("unpaid", bo, DefaultAcceptRequest());
        db.BillingSubscriptions.Add(new EffortlessInsight.Api.Data.Entities.Billing.BillingSubscription {
            OrganizationId = invite.CaOrganizationId, Status = "active", CurrentPeriodEnd = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();
        var act = () => service.AcceptInvitationLinkAsync("unpaid", bo, new(destination.OrganizationId));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("SUBSCRIPTION_REQUIRED");
        invite.Status.Should().Be("pending");
        prospect.Status.Should().Be("staging");
        db.OrganizationMembers.Should().NotContain(m => m.OrganizationId == destination.OrganizationId && m.UserId == ca);
    }

    [Fact]
    public async Task RepeatedAcceptanceReturnsReceiptWithoutDuplicatingMembership()
    {
        var (service, db, ca, bo, _) = CreateService();
        var (invite, _) = SeedPendingInvitation(db, ca, HashTokenForTest("retry"));
        db.Notices.Add(new Notice { OrganizationId = invite.CaOrganizationId, UploadedById = ca,
            Gstin = TestGstin, FileName = "notice.pdf", FileUrl = "original", ProcessingStatus = NoticeProcessingStatus.Completed });
        await db.SaveChangesAsync();
        await PreparePaidOrganization(service, db, "retry", bo);
        var first = await service.AcceptInvitationAsync("retry", bo, DefaultAcceptRequest());
        var retry = await service.AcceptInvitationAsync("retry", bo, DefaultAcceptRequest());
        retry.MergedNoticeCount.Should().Be(first.MergedNoticeCount).And.Be(1);
        db.OrganizationMembers.Count(m => m.OrganizationId == first.OrganizationId && m.UserId == ca).Should().Be(1);
        db.Notices.Count().Should().Be(1);
        db.AuditLogs.Count(a => a.Action == "ca_client_invitation.handover").Should().Be(1);
    }

    [Fact]
    public async Task RepairOfOldAcceptanceDoesNotRestoreRevokedCaMembership()
    {
        var (service, db, ca, bo, org) = CreateService();
        var (invite, prospect) = SeedPendingInvitation(db, ca, HashTokenForTest("repair"));
        await PreparePaidOrganization(service, db, "repair", bo);
        invite.Status = "accepted";
        invite.AcceptedUserId = bo;
        invite.ResultingOrganizationId = org;
        prospect.Status = "merged";
        prospect.MergedIntoOrganizationId = org;
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = org, UserId = ca, Role = "ca", Status = "removed", IsExternal = true });
        db.Notices.Add(new Notice { OrganizationId = invite.CaOrganizationId, UploadedById = ca,
            Gstin = TestGstin, FileName = "legacy.pdf", FileUrl = "original", ProcessingStatus = NoticeProcessingStatus.Completed });
        db.SaveChanges(); // simulate data left by the old implementation
        await service.AcceptInvitationLinkAsync("repair", bo, new(org));
        db.Notices.Single().OrganizationId.Should().Be(org);
        db.OrganizationMembers.Single(m => m.OrganizationId == org && m.UserId == ca).Status.Should().Be("removed");
    }

    [Fact]
    public async Task AcceptInvitationAsync_MergesStagedNotices_WithZeroDuplicates()
    {
        var (service, db, caUserId, boUserId, orgId) = CreateService();
        const string token = "test-token-1";
        var tokenHash = HashTokenForTest(token);
        var (invitation, prospectClient) = SeedPendingInvitation(db, caUserId, tokenHash);

        // Two uploads of the same file (same FileHash) plus one distinct file.
        db.CaStagedNotices.AddRange(
            new CaStagedNotice
            {
                CaProspectClientId = prospectClient.Id,
                FileHash = "hash-A",
                FileUrl = "s3://a1",
                FileName = "notice-a-first-upload.pdf",
                FileSize = 100,
                UploadedByUserId = caUserId,
                UploadedAt = DateTime.UtcNow
            },
            new CaStagedNotice
            {
                CaProspectClientId = prospectClient.Id,
                FileHash = "hash-A",
                FileUrl = "s3://a2",
                FileName = "notice-a-reupload.pdf",
                FileSize = 100,
                UploadedByUserId = caUserId,
                UploadedAt = DateTime.UtcNow
            },
            new CaStagedNotice
            {
                CaProspectClientId = prospectClient.Id,
                FileHash = "hash-B",
                FileUrl = "s3://b1",
                FileName = "notice-b.pdf",
                FileSize = 200,
                UploadedByUserId = caUserId,
                UploadedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        await PreparePaidOrganization(service, db, token, boUserId);
        var result = await service.AcceptInvitationAsync(token, boUserId, DefaultAcceptRequest());

        result.OrganizationId.Should().Be(orgId);
        result.NewNoticeCount.Should().Be(2, "hash-A and hash-B are distinct files");
        result.MergedNoticeCount.Should().Be(0, "legacy conversions are reported as new canonical notices");

        var notices = db.Notices.Where(n => n.OrganizationId == orgId).ToList();
        notices.Should().HaveCount(2);

        var staged = db.CaStagedNotices.Where(n => n.CaProspectClientId == prospectClient.Id).ToList();
        staged.Should().OnlyContain(n => n.MergedToNotices);
        var hashAStaged = staged.Where(n => n.FileHash == "hash-A").ToList();
        hashAStaged.Should().HaveCount(2);
        hashAStaged[0].MergedNoticeId.Should().Be(hashAStaged[1].MergedNoticeId,
            "both hash-A uploads must resolve to the same single Notice");
    }

    [Fact]
    public async Task AcceptInvitationAsync_CreatesActiveCaMembership_WithAccessExpiry()
    {
        var (service, db, caUserId, boUserId, orgId) = CreateService();
        const string token = "test-token-2";
        var tokenHash = HashTokenForTest(token);
        SeedPendingInvitation(db, caUserId, tokenHash, accessDurationDays: 90);

        await PreparePaidOrganization(service, db, token, boUserId);
        await service.AcceptInvitationAsync(token, boUserId, DefaultAcceptRequest());

        var caMembership = db.OrganizationMembers.Single(m => m.OrganizationId == orgId && m.UserId == caUserId);
        caMembership.Role.Should().Be("ca");
        caMembership.IsExternal.Should().BeTrue();
        caMembership.Status.Should().Be("active");
        caMembership.AccessExpiresAt.Should().NotBeNull();
        caMembership.AccessExpiresAt!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(90), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task AcceptInvitationAsync_MarksInvitationAcceptedAndProspectMerged()
    {
        var (service, db, caUserId, boUserId, orgId) = CreateService();
        const string token = "test-token-3";
        var tokenHash = HashTokenForTest(token);
        var (invitation, prospectClient) = SeedPendingInvitation(db, caUserId, tokenHash);

        await PreparePaidOrganization(service, db, token, boUserId);
        await service.AcceptInvitationAsync(token, boUserId, DefaultAcceptRequest());

        var reloadedInvitation = db.CaClientInvitations.Single(i => i.Id == invitation.Id);
        reloadedInvitation.Status.Should().Be("accepted");
        reloadedInvitation.ResultingOrganizationId.Should().Be(orgId);
        reloadedInvitation.AcceptedUserId.Should().Be(boUserId);

        var reloadedProspect = db.CaProspectClients.Single(p => p.Id == prospectClient.Id);
        reloadedProspect.Status.Should().Be("merged");
        reloadedProspect.MergedIntoOrganizationId.Should().Be(orgId);
    }

    [Fact]
    public async Task AcceptInvitationAsync_EmailMismatch_Throws()
    {
        var (service, db, caUserId, boUserId, _) = CreateService();
        const string token = "test-token-4";
        var tokenHash = HashTokenForTest(token);
        SeedPendingInvitation(db, caUserId, tokenHash, boEmail: "someone-else@example.com");

        var act = () => service.AcceptInvitationAsync(token, boUserId, DefaultAcceptRequest());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("EMAIL_MISMATCH");
    }

    [Fact]
    public async Task AcceptInvitationAsync_AlreadyAccepted_Throws()
    {
        var (service, db, caUserId, boUserId, _) = CreateService();
        const string token = "test-token-5";
        var tokenHash = HashTokenForTest(token);
        var (invitation, _) = SeedPendingInvitation(db, caUserId, tokenHash);
        invitation.Status = "accepted";
        await db.SaveChangesAsync();

        var act = () => service.AcceptInvitationAsync(token, boUserId, DefaultAcceptRequest());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("INVITATION_ACCEPTED");
    }

    [Fact]
    public async Task AcceptInvitationAsync_Expired_MarksExpiredAndThrows()
    {
        var (service, db, caUserId, boUserId, _) = CreateService();
        const string token = "test-token-6";
        var tokenHash = HashTokenForTest(token);
        var (invitation, _) = SeedPendingInvitation(db, caUserId, tokenHash);
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var act = () => service.AcceptInvitationAsync(token, boUserId, DefaultAcceptRequest());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("INVITATION_EXPIRED");

        db.CaClientInvitations.Single(i => i.Id == invitation.Id).Status.Should().Be("expired");
    }

    [Fact]
    public async Task DeclineInvitationAsync_MarksDeclined_AndKeepsStagedData()
    {
        var (service, db, caUserId, boUserId, _) = CreateService();
        const string token = "test-token-7";
        var tokenHash = HashTokenForTest(token);
        var (invitation, prospectClient) = SeedPendingInvitation(db, caUserId, tokenHash);
        db.CaStagedNotices.Add(new CaStagedNotice
        {
            CaProspectClientId = prospectClient.Id,
            FileHash = "hash-X",
            FileUrl = "s3://x",
            FileName = "notice-x.pdf",
            UploadedByUserId = caUserId,
            UploadedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await service.DeclineInvitationAsync(token, boUserId);

        db.CaClientInvitations.Single(i => i.Id == invitation.Id).Status.Should().Be("declined");
        db.CaProspectClients.Single(p => p.Id == prospectClient.Id).Status.Should().Be("staging");
        db.CaStagedNotices.Should().ContainSingle(n => !n.MergedToNotices);
    }

    private static string HashTokenForTest(string token)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
