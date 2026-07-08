using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.Data.Entities.Billing;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace EffortlessInsight.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext? _tenantContext;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Current tenant organization ID for query filtering.
    /// </summary>
    private Guid? CurrentOrganizationId => _tenantContext?.OrganizationId;

    /// <summary>
    /// Whether to bypass tenant filtering (for admin operations).
    /// </summary>
    private bool BypassTenantFilter => _tenantContext?.BypassTenantFilter ?? true;

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<OrganizationGstin> OrganizationGstins => Set<OrganizationGstin>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();
    public DbSet<GstinStateCode> GstinStateCodes => Set<GstinStateCode>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeRelationship> NoticeRelationships => Set<NoticeRelationship>();
    public DbSet<NoticeAiReport> NoticeAiReports => Set<NoticeAiReport>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<NoticeTask> Tasks => Set<NoticeTask>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<NoticeResponse> NoticeResponses => Set<NoticeResponse>();
    public DbSet<DeadlineReminder> DeadlineReminders => Set<DeadlineReminder>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Embedding> Embeddings => Set<Embedding>();
    public DbSet<KnowledgeBase> KnowledgeBase => Set<KnowledgeBase>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();
    public DbSet<PasswordHistory> PasswordHistory => Set<PasswordHistory>();
    public DbSet<UserOAuthProvider> UserOAuthProviders => Set<UserOAuthProvider>();

    // Token Management entities
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    // Task & Collaboration entities
    public DbSet<TaskAssignee> TaskAssignees => Set<TaskAssignee>();
    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();
    public DbSet<CommentEditHistory> CommentEditHistory => Set<CommentEditHistory>();
    public DbSet<CommentReaction> CommentReactions => Set<CommentReaction>();
    public DbSet<DocumentRequest> DocumentRequests => Set<DocumentRequest>();
    public DbSet<DocumentRequestDocument> DocumentRequestDocuments => Set<DocumentRequestDocument>();
    public DbSet<DocumentRequestTemplate> DocumentRequestTemplates => Set<DocumentRequestTemplate>();
    public DbSet<NoticeFile> NoticeFiles => Set<NoticeFile>();
    public DbSet<FileFolder> FileFolders => Set<FileFolder>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    // Task Dependencies, Reminders, and Time Tracking
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskReminder> TaskReminders => Set<TaskReminder>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    // Workflow Engine entities
    public DbSet<WorkflowTemplate> WorkflowTemplates => Set<WorkflowTemplate>();
    public DbSet<WorkflowStage> WorkflowStages => Set<WorkflowStage>();
    public DbSet<WorkflowAssignmentRule> WorkflowAssignmentRules => Set<WorkflowAssignmentRule>();
    public DbSet<WorkflowEscalationRule> WorkflowEscalationRules => Set<WorkflowEscalationRule>();
    public DbSet<NoticeWorkflowInstance> NoticeWorkflowInstances => Set<NoticeWorkflowInstance>();
    public DbSet<WorkflowHistory> WorkflowHistories => Set<WorkflowHistory>();
    public DbSet<WorkflowStageInstance> WorkflowStageInstances => Set<WorkflowStageInstance>();
    public DbSet<NoticeDeadline> NoticeDeadlines => Set<NoticeDeadline>();
    public DbSet<DeadlineExtension> DeadlineExtensions => Set<DeadlineExtension>();
    public DbSet<WorkflowSlaMetric> WorkflowSlaMetrics => Set<WorkflowSlaMetric>();

    // Approval Chain entities
    public DbSet<ApprovalChain> ApprovalChains => Set<ApprovalChain>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalAction> ApprovalActions => Set<ApprovalAction>();

    // Notification Service entities
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();
    public DbSet<PushToken> PushTokens => Set<PushToken>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<ScheduledNotification> ScheduledNotifications => Set<ScheduledNotification>();
    public DbSet<EmailUnsubscribe> EmailUnsubscribes => Set<EmailUnsubscribe>();
    public DbSet<ChannelUnsubscribe> ChannelUnsubscribes => Set<ChannelUnsubscribe>();
    public DbSet<NotificationDeadLetter> NotificationDeadLetters => Set<NotificationDeadLetter>();

    // Billing Service entities
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<BillingSubscription> BillingSubscriptions => Set<BillingSubscription>();
    public DbSet<BillingDetails> BillingDetails => Set<BillingDetails>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<CouponRedemption> CouponRedemptions => Set<CouponRedemption>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    // Admin Portal entities
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<ImpersonationSession> ImpersonationSessions => Set<ImpersonationSession>();
    public DbSet<OrganizationCredit> OrganizationCredits => Set<OrganizationCredit>();
    public DbSet<CreditUsageRecord> CreditUsageRecords => Set<CreditUsageRecord>();
    public DbSet<SystemAlert> SystemAlerts => Set<SystemAlert>();
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();
    public DbSet<ContentPageVersion> ContentPageVersions => Set<ContentPageVersion>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<AdminSession> AdminSessions => Set<AdminSession>();
    public DbSet<AdminPasswordHistory> AdminPasswordHistory => Set<AdminPasswordHistory>();

    // Custom Roles and Teams
    public DbSet<CustomRole> CustomRoles => Set<CustomRole>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    // Data Export entities
    public DbSet<DataExport> DataExports => Set<DataExport>();

    // GSTN Integration entities
    public DbSet<GstnConnection> GstnConnections => Set<GstnConnection>();
    public DbSet<GstnOtpSession> GstnOtpSessions => Set<GstnOtpSession>();
    public DbSet<GstnSyncLog> GstnSyncLogs => Set<GstnSyncLog>();

    // WhatsApp Bot entities
    public DbSet<WhatsAppVerification> WhatsAppVerifications => Set<WhatsAppVerification>();
    public DbSet<WhatsAppSession> WhatsAppSessions => Set<WhatsAppSession>();
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => Set<WhatsAppMessageLog>();
    public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();

    // Reporting entities (GAP-RPT-006)
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();

    // AI Chat entities
    public DbSet<NoticeConversation> NoticeConversations => Set<NoticeConversation>();
    public DbSet<NoticeMessage> NoticeMessages => Set<NoticeMessage>();
    public DbSet<ConversationSummary> ConversationSummaries => Set<ConversationSummary>();
    public DbSet<MessageFeedback> MessageFeedbacks => Set<MessageFeedback>();
    public DbSet<AIAuditLog> AIAuditLogs => Set<AIAuditLog>();

    // GST Sync Module entities (isolated from GSTN Integration)
    public DbSet<GstClient> GstClients => Set<GstClient>();
    public DbSet<GstSyncSession> GstSyncSessions => Set<GstSyncSession>();
    public DbSet<GstNoticeRaw> GstNoticesRaw => Set<GstNoticeRaw>();
    public DbSet<GstExtensionEvent> GstExtensionEvents => Set<GstExtensionEvent>();
    public DbSet<GstSyncReminder> GstSyncReminders => Set<GstSyncReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Ignore value types that are stored as JSON (not entities)
        modelBuilder.Ignore<Citation>();

        // Configure entities
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filter for soft delete
        modelBuilder.Entity<Organization>().HasQueryFilter(o => o.DeletedAt == null);
        modelBuilder.Entity<ApplicationUser>().HasQueryFilter(u => u.DeletedAt == null);

        // Global query filters with TENANT ISOLATION (defense-in-depth)
        // These filters ensure that even if code forgets to filter by OrganizationId,
        // queries will only return data for the current tenant.

        // Notice and related entities - tenant-scoped
        modelBuilder.Entity<Notice>().HasQueryFilter(n =>
            n.DeletedAt == null &&
            (BypassTenantFilter || CurrentOrganizationId == null || n.OrganizationId == CurrentOrganizationId));

        // Note: Comment, NoticeResponse, DeadlineReminder, NoticeTask, and Attachment
        // are scoped through Notice navigation. Tenant filtering is applied via Notice's filter.
        modelBuilder.Entity<Comment>().HasQueryFilter(c => c.DeletedAt == null);
        modelBuilder.Entity<NoticeResponse>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<DeadlineReminder>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<NoticeTask>().HasQueryFilter(t => t.DeletedAt == null);
        modelBuilder.Entity<Attachment>().HasQueryFilter(a => a.DeletedAt == null);

        // Workflow entities - tenant-scoped
        modelBuilder.Entity<WorkflowTemplate>().HasQueryFilter(w =>
            w.DeletedAt == null &&
            (BypassTenantFilter || CurrentOrganizationId == null || w.OrganizationId == null || w.OrganizationId == CurrentOrganizationId));

        modelBuilder.Entity<WorkflowStage>().HasQueryFilter(s => s.DeletedAt == null);
        modelBuilder.Entity<WorkflowAssignmentRule>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<WorkflowEscalationRule>().HasQueryFilter(r => r.DeletedAt == null);

        // Note: NoticeWorkflowInstance, WorkflowHistory, and NoticeDeadline
        // are scoped through Notice navigation. Tenant filtering is applied via Notice's filter.
        modelBuilder.Entity<NoticeWorkflowInstance>().HasQueryFilter(i => i.DeletedAt == null);
        modelBuilder.Entity<WorkflowHistory>().HasQueryFilter(h => h.DeletedAt == null);
        modelBuilder.Entity<NoticeDeadline>().HasQueryFilter(d => d.DeletedAt == null);

        modelBuilder.Entity<DeadlineExtension>().HasQueryFilter(e => e.DeletedAt == null);
        modelBuilder.Entity<WorkflowStageInstance>().HasQueryFilter(si => si.DeletedAt == null);

        modelBuilder.Entity<WorkflowSlaMetric>().HasQueryFilter(m =>
            m.DeletedAt == null &&
            (BypassTenantFilter || CurrentOrganizationId == null || m.OrganizationId == CurrentOrganizationId));

        // Approval Chain entities
        modelBuilder.Entity<ApprovalChain>().HasQueryFilter(c =>
            c.DeletedAt == null &&
            (BypassTenantFilter || CurrentOrganizationId == null || c.OrganizationId == CurrentOrganizationId));
        modelBuilder.Entity<ApprovalStep>().HasQueryFilter(s => s.DeletedAt == null);
        modelBuilder.Entity<ApprovalRequest>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<ApprovalAction>().HasQueryFilter(a => a.DeletedAt == null);

        // Task Dependencies, Reminders, and Time Entries
        modelBuilder.Entity<TaskDependency>().HasQueryFilter(d => d.DeletedAt == null);
        modelBuilder.Entity<TaskReminder>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<TimeEntry>().HasQueryFilter(e => e.DeletedAt == null);

        // Configure JSON columns for Dictionary properties
        modelBuilder.Entity<ApplicationUser>()
            .Property(u => u.Preferences)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Notice>()
            .Property(n => n.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.OldValues)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.NewValues)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Plan>()
            .Property(p => p.Features)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Embedding>()
            .Property(e => e.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<KnowledgeBase>()
            .Property(k => k.Metadata)
            .HasColumnType("jsonb");

        // Configure JSON columns for array/list properties
        modelBuilder.Entity<ApplicationUser>()
            .Property(u => u.BackupCodesHash)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Notice>()
            .Property(n => n.Tags)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Comment>()
            .Property(c => c.Mentions)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Comment>()
            .Property(c => c.AttachmentUrls)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Organization>()
            .Property(o => o.Gstins)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Organization>()
            .Property(o => o.Settings)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeAiReport>()
            .Property(r => r.ActionItems)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeAiReport>()
            .Property(r => r.RequiredDocuments)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeAiReport>()
            .Property(r => r.LegalReferences)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeAiReport>()
            .Property(r => r.ConfidenceScores)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeAiReport>()
            .Property(r => r.FullReportJson)
            .HasColumnType("jsonb");

        // Workflow Engine JSON columns
        modelBuilder.Entity<WorkflowTemplate>()
            .Property(w => w.ApplicableNoticeTypes)
            .HasColumnType("jsonb");

        modelBuilder.Entity<WorkflowStage>()
            .Property(s => s.AllowedTransitions)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowStage>()
            .Property(s => s.EntryActions)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowStage>()
            .Property(s => s.ExitActions)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowStage>()
            .Property(s => s.AutoTransitionRules)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowStage>()
            .Property(s => s.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<WorkflowAssignmentRule>()
            .Property(r => r.Conditions)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowAssignmentRule>()
            .Property(r => r.Actions)
            .HasColumnType("jsonb");

        modelBuilder.Entity<WorkflowEscalationRule>()
            .Property(r => r.Actions)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeWorkflowInstance>()
            .Property(i => i.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<WorkflowHistory>()
            .Property(h => h.EventData)
            .HasColumnType("jsonb");

        modelBuilder.Entity<NoticeDeadline>()
            .Property(d => d.ReminderDaysBefore)
            .HasColumnType("jsonb");
        modelBuilder.Entity<NoticeDeadline>()
            .Property(d => d.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<DeadlineExtension>()
            .Property(e => e.SupportingDocumentIds)
            .HasColumnType("jsonb");
        modelBuilder.Entity<DeadlineExtension>()
            .Property(e => e.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<WorkflowStageInstance>()
            .Property(si => si.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<WorkflowSlaMetric>()
            .Property(m => m.AssigneeBreakdown)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowSlaMetric>()
            .Property(m => m.NoticeTypeBreakdown)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowSlaMetric>()
            .Property(m => m.PriorityBreakdown)
            .HasColumnType("jsonb");

        // Approval Chain JSON columns
        modelBuilder.Entity<ApprovalChain>()
            .Property(c => c.TriggerConditions)
            .HasColumnType("jsonb");
        modelBuilder.Entity<ApprovalStep>()
            .Property(s => s.Conditions)
            .HasColumnType("jsonb");
        modelBuilder.Entity<ApprovalRequest>()
            .Property(r => r.Metadata)
            .HasColumnType("jsonb");

        // AI Chat JSON columns
        modelBuilder.Entity<NoticeMessage>()
            .Property(m => m.Citations)
            .HasColumnType("jsonb");

        // Configure vector column for text-embedding-ada-002 (1536 dimensions)
        // Note: text-embedding-3-large uses 3072, but ada-002 is more cost-effective
        modelBuilder.Entity<Embedding>()
            .Property(e => e.Vector)
            .HasColumnType("vector(1536)");

        // ============================================================================
        // Organization Member Configuration
        // ============================================================================
        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            // Composite unique constraint: one membership per user per org
            entity.HasIndex(e => new { e.OrganizationId, e.UserId })
                .IsUnique();

            // Only one owner per organization
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"Role\" = 'owner'")
                .IsUnique()
                .HasDatabaseName("IX_OrganizationMembers_SingleOwner");

            // Performance indexes
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Role);
            entity.HasIndex(e => e.Status);

            // JSON column
            entity.Property(e => e.NotificationPreferences)
                .HasColumnType("jsonb");

            // Role constraint
            entity.Property(e => e.Role)
                .HasMaxLength(20);

            // Relationships
            entity.HasOne(e => e.Organization)
                .WithMany(o => o.Members)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.InvitedBy)
                .WithMany()
                .HasForeignKey(e => e.InvitedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.SuspendedBy)
                .WithMany()
                .HasForeignKey(e => e.SuspendedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Organization GSTIN Configuration
        // ============================================================================
        modelBuilder.Entity<OrganizationGstin>(entity =>
        {
            // GSTIN must be unique across the platform
            entity.HasIndex(e => e.Gstin)
                .IsUnique();

            // Only one primary GSTIN per organization
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"IsPrimary\" = true")
                .IsUnique()
                .HasDatabaseName("IX_OrganizationGstins_SinglePrimary");

            // Performance indexes
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.StateCode);

            // GSTIN format check constraint (applied at database level)
            // Note: Constraint applies to decrypted value during validation
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_OrganizationGstins_Gstin_Format",
                "\"Gstin\" ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$' OR \"Gstin\" LIKE 'ENC:%'"
            ));

            // Apply field encryption for GSTIN (DPDP Act compliance)
            entity.Property(e => e.Gstin)
                .HasConversion(new Services.Encryption.EncryptedStringConverter());

            // Relationship
            entity.HasOne(e => e.Organization)
                .WithMany(o => o.OrganizationGstins)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Organization Invitation Configuration
        // ============================================================================
        modelBuilder.Entity<OrganizationInvitation>(entity =>
        {
            // Token hash must be unique
            entity.HasIndex(e => e.TokenHash)
                .IsUnique();

            // Prevent duplicate pending invitations for same email in same org
            entity.HasIndex(e => new { e.OrganizationId, e.EmailNormalized })
                .HasFilter("\"Status\" = 'pending'")
                .IsUnique()
                .HasDatabaseName("IX_OrganizationInvitations_PendingUnique");

            // Performance indexes
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.EmailNormalized);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt)
                .HasFilter("\"Status\" = 'pending'");

            // Relationships
            entity.HasOne(e => e.Organization)
                .WithMany(o => o.Invitations)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.InvitedBy)
                .WithMany()
                .HasForeignKey(e => e.InvitedById)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AcceptedUser)
                .WithMany()
                .HasForeignKey(e => e.AcceptedUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // GSTIN State Code Configuration (Reference Table)
        // ============================================================================
        modelBuilder.Entity<GstinStateCode>(entity =>
        {
            entity.HasKey(e => e.Code);
        });

        // ============================================================================
        // Organization Configuration Updates
        // ============================================================================
        modelBuilder.Entity<Organization>(entity =>
        {
            // Unique normalized name (excluding soft-deleted)
            entity.HasIndex(e => e.NameNormalized)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_Organizations_NameNormalized_Unique");

            // Performance indexes
            entity.HasIndex(e => e.State)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.SubscriptionStatus)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CreatedAt);

            // Apply field encryption for PAN and TAN (DPDP Act compliance)
            entity.Property(e => e.Pan)
                .HasConversion(new Services.Encryption.EncryptedStringConverter());
            entity.Property(e => e.Tan)
                .HasConversion(new Services.Encryption.EncryptedStringConverter());
        });

        // ============================================================================
        // Notice Configuration
        // ============================================================================
        modelBuilder.Entity<Notice>(entity =>
        {
            // Basic indexes
            entity.HasIndex(n => n.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.ResponseDeadline)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.Gstin)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.Priority)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.AssignedToId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.CreatedAt)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(n => n.FileHash)
                .HasFilter("\"DeletedAt\" IS NULL");

            // Index for GSTN portal duplicate detection
            entity.HasIndex(n => new { n.OrganizationId, n.GstnNoticeId })
                .HasFilter("\"DeletedAt\" IS NULL AND \"GstnNoticeId\" IS NOT NULL")
                .HasDatabaseName("IX_Notices_Org_GstnNoticeId");

            // Composite index for common queries
            entity.HasIndex(n => new { n.OrganizationId, n.Status, n.ResponseDeadline })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_Notices_Org_Status_Deadline");

            // Full-text search index on notice number
            // Uses PostgreSQL B-tree index for efficient search
            entity.HasIndex(n => n.NoticeNumber)
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_Notices_Number_Search");

            // Relationships
            entity.HasOne(n => n.AssignedBy)
                .WithMany()
                .HasForeignKey(n => n.AssignedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(n => n.DeletedBy)
                .WithMany()
                .HasForeignKey(n => n.DeletedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(n => n.GstinNavigation)
                .WithMany()
                .HasForeignKey(n => n.GstinId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Notice Relationship Configuration
        // ============================================================================
        modelBuilder.Entity<NoticeRelationship>(entity =>
        {
            entity.HasIndex(r => r.SourceNoticeId);
            entity.HasIndex(r => r.TargetNoticeId);
            entity.HasIndex(r => new { r.SourceNoticeId, r.TargetNoticeId, r.RelationshipType })
                .IsUnique()
                .HasDatabaseName("IX_NoticeRelationships_Unique");

            entity.HasOne(r => r.SourceNotice)
                .WithMany(n => n.OutgoingRelationships)
                .HasForeignKey(r => r.SourceNoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.TargetNotice)
                .WithMany(n => n.IncomingRelationships)
                .HasForeignKey(r => r.TargetNoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.CreatedBy)
                .WithMany()
                .HasForeignKey(r => r.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ============================================================================
        // Attachment Configuration (Versioning)
        // ============================================================================
        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.HasIndex(a => a.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(a => a.ResponseId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(a => new { a.NoticeId, a.IsCurrentVersion })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsCurrentVersion\" = true")
                .HasDatabaseName("IX_Attachments_NoticeId_Current");
            entity.HasIndex(a => a.OriginalAttachmentId)
                .HasFilter("\"DeletedAt\" IS NULL");

            // Self-referencing relationship for version history
            entity.HasOne(a => a.PreviousVersion)
                .WithMany()
                .HasForeignKey(a => a.PreviousVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Embedding>()
            .HasIndex(e => e.SourceType);

        // User Sessions indexes
        modelBuilder.Entity<UserSession>()
            .HasIndex(s => s.UserId);
        modelBuilder.Entity<UserSession>()
            .HasIndex(s => s.RefreshTokenJti)
            .IsUnique();
        modelBuilder.Entity<UserSession>()
            .HasIndex(s => s.ExpiresAt);

        // Login Audit indexes
        modelBuilder.Entity<LoginAudit>()
            .HasIndex(l => l.UserId);
        modelBuilder.Entity<LoginAudit>()
            .HasIndex(l => l.IpAddress);
        modelBuilder.Entity<LoginAudit>()
            .HasIndex(l => l.CreatedAt);

        // Password History indexes
        modelBuilder.Entity<PasswordHistory>()
            .HasIndex(p => p.UserId);

        // UserOAuthProvider configuration
        modelBuilder.Entity<UserOAuthProvider>(entity =>
        {
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.Provider, e.ProviderId })
                .IsUnique()
                .HasDatabaseName("IX_UserOAuthProviders_Provider_ProviderId");
            entity.HasIndex(e => new { e.UserId, e.Provider })
                .IsUnique()
                .HasDatabaseName("IX_UserOAuthProviders_UserId_Provider");
        });

        // ============================================================================
        // NoticeResponse Configuration
        // ============================================================================
        modelBuilder.Entity<NoticeResponse>(entity =>
        {
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.NoticeId, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_NoticeResponses_NoticeId_Status");
            entity.HasIndex(e => new { e.NoticeId, e.CreatedAt })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_NoticeResponses_NoticeId_CreatedAt");
        });

        // ============================================================================
        // DeadlineReminder Configuration
        // ============================================================================
        modelBuilder.Entity<DeadlineReminder>(entity =>
        {
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.RemindAt, e.IsSent })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsSent\" = false")
                .HasDatabaseName("IX_DeadlineReminders_Pending");
            entity.HasIndex(e => e.UserId)
                .HasFilter("\"DeletedAt\" IS NULL");
        });

        // ============================================================================
        // NoticeTask Configuration
        // ============================================================================
        modelBuilder.Entity<NoticeTask>(entity =>
        {
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.NoticeId, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_NoticeTasks_NoticeId_Status");
            entity.HasIndex(e => e.AssignedToId)
                .HasFilter("\"DeletedAt\" IS NULL");
        });

        // ============================================================================
        // Audit Log Configuration
        // ============================================================================
        modelBuilder.Entity<AuditLog>(entity =>
        {
            // JSON columns
            entity.Property(e => e.OldValues)
                .HasColumnType("jsonb");
            entity.Property(e => e.NewValues)
                .HasColumnType("jsonb");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            // Performance indexes
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.CreatedAt);

            // Composite index for common queries
            entity.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
            entity.HasIndex(e => new { e.EntityType, e.EntityId });

            // Relationships (optional - no cascade delete)
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Workflow Template Configuration
        // ============================================================================
        modelBuilder.Entity<WorkflowTemplate>(entity =>
        {
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsSystem)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.Name })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_WorkflowTemplates_Org_Name_Unique");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Workflow Stage Configuration
        // ============================================================================
        modelBuilder.Entity<WorkflowStage>(entity =>
        {
            entity.HasIndex(e => e.WorkflowTemplateId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.WorkflowTemplateId, e.StageKey })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_WorkflowStages_Template_StageKey_Unique");
            entity.HasIndex(e => new { e.WorkflowTemplateId, e.StageOrder })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_WorkflowStages_Template_Order");

            entity.HasOne(e => e.WorkflowTemplate)
                .WithMany(t => t.Stages)
                .HasForeignKey(e => e.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Workflow Assignment Rule Configuration
        // ============================================================================
        modelBuilder.Entity<WorkflowAssignmentRule>(entity =>
        {
            entity.HasIndex(e => e.WorkflowTemplateId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.WorkflowTemplateId, e.Priority })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsEnabled\" = true")
                .HasDatabaseName("IX_WorkflowAssignmentRules_Template_Priority");

            entity.HasOne(e => e.WorkflowTemplate)
                .WithMany(t => t.AssignmentRules)
                .HasForeignKey(e => e.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Workflow Escalation Rule Configuration
        // ============================================================================
        modelBuilder.Entity<WorkflowEscalationRule>(entity =>
        {
            entity.HasIndex(e => e.WorkflowTemplateId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.WorkflowTemplateId, e.TriggerPercent })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_WorkflowEscalationRules_Template_Trigger");

            entity.HasOne(e => e.WorkflowTemplate)
                .WithMany(t => t.EscalationRules)
                .HasForeignKey(e => e.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Notice Workflow Instance Configuration
        // ============================================================================
        modelBuilder.Entity<NoticeWorkflowInstance>(entity =>
        {
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.WorkflowTemplateId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.SlaStatus)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.AssignedToId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.SlaDeadline)
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" = 'active'")
                .HasDatabaseName("IX_NoticeWorkflowInstances_ActiveSlaDeadline");
            entity.HasIndex(e => new { e.NoticeId, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_NoticeWorkflowInstances_Notice_Status");

            // Only one active workflow instance per notice
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" = 'active'")
                .IsUnique()
                .HasDatabaseName("IX_NoticeWorkflowInstances_SingleActivePerNotice");

            entity.HasOne(e => e.Notice)
                .WithMany()
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.WorkflowTemplate)
                .WithMany(t => t.Instances)
                .HasForeignKey(e => e.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CurrentStage)
                .WithMany()
                .HasForeignKey(e => e.CurrentStageId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.AssignedTo)
                .WithMany()
                .HasForeignKey(e => e.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PreviousAssignee)
                .WithMany()
                .HasForeignKey(e => e.PreviousAssigneeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Workflow Stage Instance Configuration (Parallel Execution Support)
        // ============================================================================
        modelBuilder.Entity<WorkflowStageInstance>(entity =>
        {
            entity.HasIndex(e => e.WorkflowInstanceId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.StageId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.BranchId)
                .HasFilter("\"DeletedAt\" IS NULL AND \"BranchId\" IS NOT NULL");
            entity.HasIndex(e => new { e.WorkflowInstanceId, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_WorkflowStageInstances_Instance_Status");
            entity.HasIndex(e => new { e.WorkflowInstanceId, e.BranchId, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_WorkflowStageInstances_Instance_Branch_Status");

            entity.HasOne(e => e.WorkflowInstance)
                .WithMany(i => i.StageInstances)
                .HasForeignKey(e => e.WorkflowInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Stage)
                .WithMany()
                .HasForeignKey(e => e.StageId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.AssignedTo)
                .WithMany()
                .HasForeignKey(e => e.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Workflow History Configuration
        // ============================================================================
        modelBuilder.Entity<WorkflowHistory>(entity =>
        {
            entity.HasIndex(e => e.WorkflowInstanceId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.EventType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.PerformedById)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CreatedAt)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.WorkflowInstanceId, e.CreatedAt })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_WorkflowHistory_Instance_CreatedAt");

            entity.HasOne(e => e.WorkflowInstance)
                .WithMany(i => i.History)
                .HasForeignKey(e => e.WorkflowInstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Notice)
                .WithMany()
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PerformedBy)
                .WithMany()
                .HasForeignKey(e => e.PerformedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Notice Deadline Configuration
        // ============================================================================
        modelBuilder.Entity<NoticeDeadline>(entity =>
        {
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.DeadlineType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.EffectiveDeadline)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.EffectiveDeadline, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" IN ('pending', 'in_progress')")
                .HasDatabaseName("IX_NoticeDeadlines_ActiveDeadlines");

            entity.HasOne(e => e.Notice)
                .WithMany()
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.VerifiedBy)
                .WithMany()
                .HasForeignKey(e => e.VerifiedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Deadline Extension Configuration
        // ============================================================================
        modelBuilder.Entity<DeadlineExtension>(entity =>
        {
            entity.HasIndex(e => e.NoticeDeadlineId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.RequestedById)
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.HasOne(e => e.NoticeDeadline)
                .WithMany(d => d.Extensions)
                .HasForeignKey(e => e.NoticeDeadlineId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Notice)
                .WithMany()
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RequestedBy)
                .WithMany()
                .HasForeignKey(e => e.RequestedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedBy)
                .WithMany()
                .HasForeignKey(e => e.ReviewedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Workflow SLA Metric Configuration
        // ============================================================================
        modelBuilder.Entity<WorkflowSlaMetric>(entity =>
        {
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.WorkflowTemplateId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.PeriodType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.WorkflowTemplateId, e.PeriodType, e.PeriodStart })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_WorkflowSlaMetrics_Org_Template_Period_Unique");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.WorkflowTemplate)
                .WithMany()
                .HasForeignKey(e => e.WorkflowTemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Task Assignee Configuration (Many-to-Many)
        // ============================================================================
        modelBuilder.Entity<TaskAssignee>(entity =>
        {
            entity.HasKey(e => new { e.TaskId, e.UserId });

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Task)
                .WithMany(t => t.Assignees)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AssignedBy)
                .WithMany()
                .HasForeignKey(e => e.AssignedById)
                .OnDelete(DeleteBehavior.SetNull);

            // Optional team reference for team-based assignment tracking
            entity.HasOne(e => e.Team)
                .WithMany()
                .HasForeignKey(e => e.TeamId)
                .HasPrincipalKey(t => t.Id)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Task Template Configuration
        // ============================================================================
        modelBuilder.Entity<TaskTemplate>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.Name })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_TaskTemplates_Org_Name_Unique");

            entity.Property(e => e.DefaultLabels)
                .HasColumnType("jsonb");
            entity.Property(e => e.ApplicableNoticeTypes)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Comment Edit History Configuration
        // ============================================================================
        modelBuilder.Entity<CommentEditHistory>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.CommentId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.CommentId, e.EditedAt })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_CommentEditHistory_Comment_EditedAt");

            entity.HasOne(e => e.Comment)
                .WithMany(c => c.EditHistory)
                .HasForeignKey(e => e.CommentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Comment Reaction Configuration
        // ============================================================================
        modelBuilder.Entity<CommentReaction>(entity =>
        {
            entity.HasKey(e => new { e.CommentId, e.UserId, e.Emoji });

            entity.HasIndex(e => e.CommentId);
            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.Comment)
                .WithMany(c => c.Reactions)
                .HasForeignKey(e => e.CommentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Constraint for allowed emojis will be enforced at application level
        });

        // ============================================================================
        // Document Request Configuration
        // ============================================================================
        modelBuilder.Entity<DocumentRequest>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.RequestedFromId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.DueDate)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.NoticeId, e.Status })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_DocumentRequests_Notice_Status");

            entity.Property(e => e.AcceptedFormats)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Notice)
                .WithMany(n => n.DocumentRequests)
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RequestedFrom)
                .WithMany()
                .HasForeignKey(e => e.RequestedFromId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.RequestedBy)
                .WithMany()
                .HasForeignKey(e => e.RequestedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReviewedBy)
                .WithMany()
                .HasForeignKey(e => e.ReviewedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Template)
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Document Request Document Configuration
        // ============================================================================
        modelBuilder.Entity<DocumentRequestDocument>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.RequestId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.FileId)
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.HasOne(e => e.Request)
                .WithMany(r => r.Documents)
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.File)
                .WithMany()
                .HasForeignKey(e => e.FileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.UploadedBy)
                .WithMany()
                .HasForeignKey(e => e.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ============================================================================
        // Document Request Template Configuration
        // ============================================================================
        modelBuilder.Entity<DocumentRequestTemplate>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.Name })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_DocumentRequestTemplates_Org_Name_Unique");

            entity.Property(e => e.AcceptedFormats)
                .HasColumnType("jsonb");
            entity.Property(e => e.ApplicableNoticeTypes)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Notice File Configuration
        // ============================================================================
        modelBuilder.Entity<NoticeFile>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.FolderId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Checksum)
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Notice)
                .WithMany(n => n.Files)
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Folder)
                .WithMany(f => f.Files)
                .HasForeignKey(e => e.FolderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.UploadedBy)
                .WithMany()
                .HasForeignKey(e => e.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ============================================================================
        // File Folder Configuration
        // ============================================================================
        modelBuilder.Entity<FileFolder>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.ParentFolderId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.NoticeId, e.Name, e.ParentFolderId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_FileFolders_Notice_Name_Parent_Unique");

            entity.HasOne(e => e.Notice)
                .WithMany(n => n.Folders)
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentFolder)
                .WithMany(f => f.SubFolders)
                .HasForeignKey(e => e.ParentFolderId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ============================================================================
        // Activity Log Configuration
        // ============================================================================
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.NoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.ActivityType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.ActorId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CreatedAt)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.NoticeId, e.CreatedAt })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_ActivityLog_Notice_CreatedAt");
            entity.HasIndex(e => new { e.OrganizationId, e.CreatedAt })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_ActivityLog_Org_CreatedAt");

            entity.Property(e => e.Data)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Notice)
                .WithMany(n => n.ActivityLogs)
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Task Dependency Configuration (GAP-TASK-001)
        // ============================================================================
        modelBuilder.Entity<TaskDependency>(entity =>
        {
            entity.HasIndex(e => e.TaskId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.DependsOnTaskId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.TaskId, e.DependsOnTaskId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_TaskDependencies_Task_DependsOn_Unique");
            entity.HasIndex(e => e.DependencyType)
                .HasFilter("\"DeletedAt\" IS NULL");

            // This task depends on the other task
            entity.HasOne(e => e.Task)
                .WithMany(t => t.DependsOn)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // The other task blocks this task
            entity.HasOne(e => e.DependsOnTask)
                .WithMany(t => t.Dependencies)
                .HasForeignKey(e => e.DependsOnTaskId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ============================================================================
        // Task Reminder Configuration (GAP-TASK-002)
        // ============================================================================
        modelBuilder.Entity<TaskReminder>(entity =>
        {
            entity.HasIndex(e => e.TaskId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.IsSent, e.DaysBeforeDue })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsSent\" = false")
                .HasDatabaseName("IX_TaskReminders_Pending");
            entity.HasIndex(e => new { e.TaskId, e.DaysBeforeDue })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_TaskReminders_Task_Days_Unique");

            entity.HasOne(e => e.Task)
                .WithMany(t => t.Reminders)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Update NoticeTask Configuration for Subtasks and Templates
        // ============================================================================
        modelBuilder.Entity<NoticeTask>(entity =>
        {
            entity.HasIndex(e => e.ParentTaskId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.NoticeId, e.ParentTaskId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_NoticeTasks_Notice_Parent");
            entity.HasIndex(e => e.DueDate)
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" NOT IN ('done', 'archived')")
                .HasDatabaseName("IX_NoticeTasks_ActiveDueDate");

            entity.Property(e => e.Labels)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.ParentTask)
                .WithMany(t => t.Subtasks)
                .HasForeignKey(e => e.ParentTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Template)
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            // Team assignment for tasks
            entity.HasOne(e => e.AssignedTeam)
                .WithMany()
                .HasForeignKey(e => e.AssignedTeamId)
                .HasPrincipalKey(t => t.Id)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.AssignedTeamId)
                .HasFilter("\"DeletedAt\" IS NULL");
        });

        // ============================================================================
        // Update Comment Configuration for Edit History and Reactions
        // ============================================================================
        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasIndex(e => e.Visibility)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsDeleted)
                .HasFilter("\"DeletedAt\" IS NULL");
        });

        // ============================================================================
        // Notification Configuration
        // ============================================================================
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.UserId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Type)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsRead)
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsRead\" = false")
                .HasDatabaseName("IX_Notifications_Unread");
            entity.HasIndex(e => e.CreatedAt)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.UserId, e.IsRead, e.CreatedAt })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_Notifications_User_Read_CreatedAt");
            entity.HasIndex(e => new { e.UserId, e.Type, e.ReferenceId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_Notifications_User_Type_Reference");

            entity.Property(e => e.Data)
                .HasColumnType("jsonb");
            entity.Property(e => e.ActionUrl)
                .HasMaxLength(2048);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Notification Delivery Configuration
        // ============================================================================
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.HasIndex(e => e.NotificationId);
            entity.HasIndex(e => e.Channel);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ProviderMessageId)
                .HasFilter("\"ProviderMessageId\" IS NOT NULL");
            entity.HasIndex(e => new { e.Channel, e.ProviderMessageId })
                .HasDatabaseName("IX_NotificationDeliveries_Channel_ProviderMessageId");
            entity.HasIndex(e => new { e.Status, e.RetryCount })
                .HasFilter("\"Status\" = 'failed' AND \"RetryCount\" < 3")
                .HasDatabaseName("IX_NotificationDeliveries_FailedRetryable");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
            entity.Property(e => e.FailureReason)
                .HasMaxLength(1000);

            entity.HasOne(e => e.Notification)
                .WithMany(n => n.Deliveries)
                .HasForeignKey(e => e.NotificationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // User Notification Preference Configuration
        // ============================================================================
        modelBuilder.Entity<UserNotificationPreference>(entity =>
        {
            entity.HasIndex(e => e.UserId)
                .IsUnique();

            entity.Property(e => e.ChannelSettings)
                .HasColumnType("jsonb");
            entity.Property(e => e.QuietHours)
                .HasColumnType("jsonb");
            entity.Property(e => e.TypePreferences)
                .HasColumnType("jsonb");
            entity.Property(e => e.DigestSettings)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserNotificationPreference>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Push Token Configuration
        // ============================================================================
        modelBuilder.Entity<PushToken>(entity =>
        {
            entity.HasIndex(e => e.Token)
                .IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"IsActive\" = true");
            entity.HasIndex(e => new { e.UserId, e.Platform, e.IsActive })
                .HasDatabaseName("IX_PushTokens_User_Platform_Active");

            entity.Property(e => e.Token)
                .HasMaxLength(500);
            entity.Property(e => e.Platform)
                .HasMaxLength(20);
            entity.Property(e => e.DeviceInfo)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Notification Template Configuration
        // ============================================================================
        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.HasIndex(e => new { e.Type, e.Channel, e.Language })
                .HasFilter("\"IsActive\" = true")
                .HasDatabaseName("IX_NotificationTemplates_Type_Channel_Language_Active");
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Type)
                .HasMaxLength(100);
            entity.Property(e => e.Channel)
                .HasMaxLength(50);
            entity.Property(e => e.Language)
                .HasMaxLength(10)
                .HasDefaultValue("en");
            entity.Property(e => e.Subject)
                .HasMaxLength(500);
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
        });

        // ============================================================================
        // Scheduled Notification Configuration
        // ============================================================================
        modelBuilder.Entity<ScheduledNotification>(entity =>
        {
            entity.HasIndex(e => e.ScheduledFor)
                .HasFilter("\"Status\" = 'pending'")
                .HasDatabaseName("IX_ScheduledNotifications_Pending");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.Type)
                .HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("pending");
            entity.Property(e => e.Data)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SentNotification)
                .WithMany()
                .HasForeignKey(e => e.SentNotificationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Email Unsubscribe Configuration
        // ============================================================================
        modelBuilder.Entity<EmailUnsubscribe>(entity =>
        {
            entity.HasIndex(e => e.Email)
                .IsUnique();
            entity.HasIndex(e => e.UserId);

            entity.Property(e => e.Email)
                .HasMaxLength(255);
            entity.Property(e => e.Reason)
                .HasMaxLength(500);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Billing Service Configurations
        // ============================================================================

        // SubscriptionPlan Configuration
        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.Code)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_SubscriptionPlans_Code_Unique");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.SortOrder)
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsActive\" = true");

            entity.OwnsOne(e => e.Limits, limits =>
            {
                limits.ToJson();
            });
            entity.Property(e => e.Features)
                .HasColumnType("jsonb");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
        });

        // BillingSubscription Configuration
        modelBuilder.Entity<BillingSubscription>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_BillingSubscriptions_Org_Unique");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CurrentPeriodEnd)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.RazorpaySubscriptionId)
                .HasFilter("\"RazorpaySubscriptionId\" IS NOT NULL");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithOne()
                .HasForeignKey<BillingSubscription>(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Plan)
                .WithMany()
                .HasForeignKey(e => e.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // BillingDetails Configuration
        modelBuilder.Entity<BillingDetails>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_BillingDetails_Org_Unique");

            entity.HasOne(e => e.Organization)
                .WithOne()
                .HasForeignKey<BillingDetails>(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PaymentMethod Configuration
        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.IsDefault })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsDefault\" = true")
                .IsUnique()
                .HasDatabaseName("IX_PaymentMethods_Org_Default_Unique");
            entity.HasIndex(e => e.RazorpayTokenId)
                .HasFilter("\"RazorpayTokenId\" IS NOT NULL");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Invoice Configuration
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.InvoiceNumber)
                .IsUnique()
                .HasDatabaseName("IX_Invoices_Number_Unique");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.InvoiceDate)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.RazorpayInvoiceId)
                .HasFilter("\"RazorpayInvoiceId\" IS NOT NULL");

            entity.Property(e => e.BillingDetails)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Subscription)
                .WithMany(s => s.Invoices)
                .HasForeignKey(e => e.SubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // InvoiceLineItem Configuration
        modelBuilder.Entity<InvoiceLineItem>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.InvoiceId)
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Invoice)
                .WithMany(i => i.LineItems)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Payment Configuration
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.RazorpayPaymentId)
                .HasFilter("\"RazorpayPaymentId\" IS NOT NULL");
            entity.HasIndex(e => e.RazorpayOrderId)
                .HasFilter("\"RazorpayOrderId\" IS NOT NULL");

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Invoice)
                .WithMany(i => i.Payments)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Subscription)
                .WithMany(s => s.Payments)
                .HasForeignKey(e => e.SubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // UsageRecord Configuration
        modelBuilder.Entity<UsageRecord>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.PeriodStart, e.PeriodEnd })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_UsageRecords_Org_Period_Unique");

            entity.Property(e => e.AdditionalMetrics)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Coupon Configuration
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.Code)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_Coupons_Code_Unique");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.ValidFrom, e.ValidUntil })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsActive\" = true")
                .HasDatabaseName("IX_Coupons_ValidDates");

            entity.Property(e => e.ApplicablePlans)
                .HasColumnType("jsonb");
            entity.Property(e => e.ApplicableCycles)
                .HasColumnType("jsonb");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
        });

        // CouponRedemption Configuration
        modelBuilder.Entity<CouponRedemption>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.CouponId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.CouponId, e.OrganizationId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_CouponRedemptions_Coupon_Org_Unique");

            entity.HasOne(e => e.Coupon)
                .WithMany(c => c.Redemptions)
                .HasForeignKey(e => e.CouponId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Subscription)
                .WithMany(s => s.CouponRedemptions)
                .HasForeignKey(e => e.SubscriptionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Invoice)
                .WithMany()
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RedeemedBy)
                .WithMany()
                .HasForeignKey(e => e.RedeemedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // WebhookEvent Configuration
        modelBuilder.Entity<WebhookEvent>(entity =>
        {
            entity.HasIndex(e => new { e.Provider, e.EventId })
                .IsUnique()
                .HasDatabaseName("IX_WebhookEvents_Provider_EventId_Unique");
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.Status, e.AttemptCount })
                .HasFilter("\"Status\" = 'failed' AND \"AttemptCount\" < 5")
                .HasDatabaseName("IX_WebhookEvents_FailedRetryable");

            entity.Property(e => e.ProcessingResult)
                .HasColumnType("jsonb");
        });

        // ============================================================================
        // Admin Portal Entity Configurations
        // ============================================================================

        // AdminUser Configuration
        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.Email)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_AdminUsers_Email_Unique");
            entity.HasIndex(e => e.EmailNormalized)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_AdminUsers_EmailNormalized_Unique");
            entity.HasIndex(e => e.Role)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.Property(e => e.Permissions)
                .HasColumnType("jsonb");
            entity.Property(e => e.BackupCodesHash)
                .HasColumnType("jsonb");
            entity.Property(e => e.IpWhitelist)
                .HasColumnType("jsonb");
        });

        // AdminAuditLog Configuration
        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.HasIndex(e => e.AdminUserId);
            entity.HasIndex(e => e.Action);
            entity.HasIndex(e => new { e.TargetType, e.TargetId })
                .HasDatabaseName("IX_AdminAuditLogs_Target");
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.AdminUserId, e.CreatedAt })
                .HasDatabaseName("IX_AdminAuditLogs_Admin_Created");

            entity.Property(e => e.Details)
                .HasColumnType("jsonb");
        });

        // ImpersonationSession Configuration
        modelBuilder.Entity<ImpersonationSession>(entity =>
        {
            entity.HasIndex(e => e.TokenHash)
                .HasFilter("\"Status\" = 'active'")
                .IsUnique()
                .HasDatabaseName("IX_ImpersonationSessions_TokenHash_Active");
            entity.HasIndex(e => e.AdminUserId);
            entity.HasIndex(e => e.TargetUserId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt)
                .HasFilter("\"Status\" = 'active'");

            entity.Property(e => e.Permissions)
                .HasColumnType("jsonb");
            entity.Property(e => e.PagesVisited)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.TargetUser)
                .WithMany()
                .HasForeignKey(e => e.TargetUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TargetOrganization)
                .WithMany()
                .HasForeignKey(e => e.TargetOrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // OrganizationCredit Configuration
        modelBuilder.Entity<OrganizationCredit>(entity =>
        {
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt)
                .HasFilter("\"Status\" = 'active' AND \"ExpiresAt\" IS NOT NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.Status })
                .HasFilter("\"Status\" = 'active'")
                .HasDatabaseName("IX_OrgCredits_Org_Active");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // CreditUsageRecord Configuration
        modelBuilder.Entity<CreditUsageRecord>(entity =>
        {
            entity.HasIndex(e => e.OrganizationCreditId);
            entity.HasIndex(e => e.InvoiceId);

            entity.HasOne(e => e.OrganizationCredit)
                .WithMany(c => c.UsageRecords)
                .HasForeignKey(e => e.OrganizationCreditId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // SystemAlert Configuration
        modelBuilder.Entity<SystemAlert>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.AlertType);
            entity.HasIndex(e => e.Category);
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.Status, e.Priority })
                .HasFilter("\"Status\" = 'active'")
                .HasDatabaseName("IX_SystemAlerts_Active_Priority");
            entity.HasIndex(e => new { e.Source, e.Status })
                .HasDatabaseName("IX_SystemAlerts_Source_Status");

            entity.Property(e => e.Data)
                .HasColumnType("jsonb");
            entity.Property(e => e.NotifiedEmails)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.AcknowledgedBy)
                .WithMany()
                .HasForeignKey(e => e.AcknowledgedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ResolvedBy)
                .WithMany()
                .HasForeignKey(e => e.ResolvedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ContentPage Configuration
        modelBuilder.Entity<ContentPage>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.Slug)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_ContentPages_Slug_Unique");
            entity.HasIndex(e => e.ContentType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Category)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.ContentType, e.Status, e.DisplayOrder })
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" = 'published'")
                .HasDatabaseName("IX_ContentPages_Published_Order");

            entity.Property(e => e.Tags)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.UpdatedBy)
                .WithMany()
                .HasForeignKey(e => e.UpdatedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.PublishedBy)
                .WithMany()
                .HasForeignKey(e => e.PublishedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ContentPageVersion Configuration
        modelBuilder.Entity<ContentPageVersion>(entity =>
        {
            entity.HasIndex(e => e.ContentPageId);
            entity.HasIndex(e => new { e.ContentPageId, e.Version })
                .IsUnique()
                .HasDatabaseName("IX_ContentPageVersions_Page_Version");

            entity.HasOne(e => e.ContentPage)
                .WithMany(p => p.Versions)
                .HasForeignKey(e => e.ContentPageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // PromptVersion Configuration
        modelBuilder.Entity<PromptVersion>(entity =>
        {
            entity.HasIndex(e => e.PromptKey);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.PromptKey, e.IsActive })
                .HasFilter("\"IsActive\" = true")
                .IsUnique()
                .HasDatabaseName("IX_PromptVersions_Key_Active_Unique");
            entity.HasIndex(e => new { e.PromptKey, e.Version })
                .IsUnique()
                .HasDatabaseName("IX_PromptVersions_Key_Version");

            entity.Property(e => e.Variables)
                .HasColumnType("jsonb");
            entity.Property(e => e.ModelConfig)
                .HasColumnType("jsonb");
            entity.Property(e => e.OutputSchema)
                .HasColumnType("jsonb");
            entity.Property(e => e.TestResults)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ActivatedBy)
                .WithMany()
                .HasForeignKey(e => e.ActivatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // AdminSession Configuration
        modelBuilder.Entity<AdminSession>(entity =>
        {
            entity.HasIndex(e => e.AdminUserId);
            entity.HasIndex(e => e.RefreshToken)
                .HasFilter("\"IsActive\" = true AND \"RefreshToken\" IS NOT NULL")
                .IsUnique()
                .HasDatabaseName("IX_AdminSessions_RefreshToken_Active");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"IsActive\" = true");
            entity.HasIndex(e => e.ExpiresAt)
                .HasFilter("\"IsActive\" = true");
            entity.HasIndex(e => new { e.AdminUserId, e.IsActive, e.ExpiresAt })
                .HasDatabaseName("IX_AdminSessions_Admin_Active_Expires");

            entity.HasOne(e => e.AdminUser)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.AdminUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Approval Chain Configuration
        // ============================================================================
        modelBuilder.Entity<ApprovalChain>(entity =>
        {
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => new { e.OrganizationId, e.IsActive })
                .HasDatabaseName("IX_ApprovalChains_Org_Active");
            entity.HasIndex(e => e.TriggerEvent);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalStep>(entity =>
        {
            entity.HasIndex(e => e.ApprovalChainId);
            entity.HasIndex(e => new { e.ApprovalChainId, e.StepOrder })
                .IsUnique()
                .HasDatabaseName("IX_ApprovalSteps_Chain_Order");

            entity.HasOne(e => e.ApprovalChain)
                .WithMany(c => c.Steps)
                .HasForeignKey(e => e.ApprovalChainId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Approver)
                .WithMany()
                .HasForeignKey(e => e.ApproverId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.EscalationUser)
                .WithMany()
                .HasForeignKey(e => e.EscalationUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.HasIndex(e => e.ApprovalChainId);
            entity.HasIndex(e => e.NoticeId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.NoticeId, e.Status })
                .HasDatabaseName("IX_ApprovalRequests_Notice_Status");
            entity.HasIndex(e => e.CurrentStepDeadline)
                .HasFilter("\"Status\" = 'pending'")
                .HasDatabaseName("IX_ApprovalRequests_Deadline_Pending");

            entity.HasOne(e => e.ApprovalChain)
                .WithMany(c => c.Requests)
                .HasForeignKey(e => e.ApprovalChainId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Notice)
                .WithMany()
                .HasForeignKey(e => e.NoticeId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Response)
                .WithMany()
                .HasForeignKey(e => e.ResponseId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.RequestedBy)
                .WithMany()
                .HasForeignKey(e => e.RequestedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovalAction>(entity =>
        {
            entity.HasIndex(e => e.ApprovalRequestId);
            entity.HasIndex(e => e.ActorId);
            entity.HasIndex(e => new { e.ApprovalRequestId, e.CreatedAt })
                .HasDatabaseName("IX_ApprovalActions_Request_Created");

            entity.HasOne(e => e.ApprovalRequest)
                .WithMany(r => r.Actions)
                .HasForeignKey(e => e.ApprovalRequestId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ApprovalStep)
                .WithMany()
                .HasForeignKey(e => e.ApprovalStepId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DelegatedTo)
                .WithMany()
                .HasForeignKey(e => e.DelegatedToId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Custom Role Configuration
        // ============================================================================
        modelBuilder.Entity<CustomRole>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            // Unique role name per organization
            entity.HasIndex(e => new { e.OrganizationId, e.NameNormalized })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_CustomRoles_Org_Name_Unique");

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsSystem)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.DisplayOrder })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsActive\" = true")
                .HasDatabaseName("IX_CustomRoles_Org_DisplayOrder");

            // JSON column for permissions
            entity.Property(e => e.Permissions)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany(o => o.CustomRoles)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Update OrganizationMember to include CustomRole relationship
        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            entity.HasOne(e => e.CustomRole)
                .WithMany(r => r.Members)
                .HasForeignKey(e => e.CustomRoleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Team Configuration
        // ============================================================================
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            // Unique team name per organization (within same parent)
            entity.HasIndex(e => new { e.OrganizationId, e.NameNormalized, e.ParentTeamId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_Teams_Org_Name_Parent_Unique");

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.ParentTeamId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.LeaderId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.HierarchyPath)
                .HasFilter("\"DeletedAt\" IS NULL");

            // JSON column for settings
            entity.Property(e => e.Settings)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany(o => o.Teams)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentTeam)
                .WithMany(t => t.SubTeams)
                .HasForeignKey(e => e.ParentTeamId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Leader)
                .WithMany()
                .HasForeignKey(e => e.LeaderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // Team Member Configuration
        // ============================================================================
        modelBuilder.Entity<TeamMember>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            // Unique user per team
            entity.HasIndex(e => new { e.TeamId, e.UserId })
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_TeamMembers_Team_User_Unique");

            // Only one primary team per user
            entity.HasIndex(e => new { e.UserId, e.IsPrimary })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsPrimary\" = true")
                .IsUnique()
                .HasDatabaseName("IX_TeamMembers_User_Primary_Unique");

            entity.HasIndex(e => e.TeamId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.UserId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Role)
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.HasOne(e => e.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(e => e.TeamId)
                .HasPrincipalKey(t => t.Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ============================================================================
        // Data Export Configuration
        // ============================================================================
        modelBuilder.Entity<DataExport>(entity =>
        {
            entity.HasQueryFilter(e => e.DeletedAt == null);

            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.RequestedById)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");

            // JSON columns for dictionary properties
            entity.Property(e => e.Options)
                .HasColumnType("jsonb");
            entity.Property(e => e.Summary)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RequestedBy)
                .WithMany()
                .HasForeignKey(e => e.RequestedById)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GSTN Integration entities
        modelBuilder.Entity<GstnConnection>(entity =>
        {
            // One connection per GSTIN
            entity.HasIndex(e => e.OrganizationGstinId)
                .IsUnique();

            // Query optimization indexes
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.NextScheduledSyncAt)
                .HasFilter("\"Status\" = 'connected' AND \"AutoSyncEnabled\" = true");
            entity.HasIndex(e => e.TokenExpiresAt)
                .HasFilter("\"Status\" = 'connected'");

            entity.HasOne(e => e.OrganizationGstin)
                .WithOne(g => g.GstnConnection)
                .HasForeignKey<GstnConnection>(e => e.OrganizationGstinId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ConnectedBy)
                .WithMany()
                .HasForeignKey(e => e.ConnectedById)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.DisconnectedBy)
                .WithMany()
                .HasForeignKey(e => e.DisconnectedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<GstnOtpSession>(entity =>
        {
            // Soft delete pattern with unique constraint
            entity.HasIndex(e => new { e.OrganizationGstinId, e.Status })
                .HasFilter("\"Status\" = 'pending'");

            entity.HasIndex(e => e.ExpiresAt);

            entity.HasOne(e => e.OrganizationGstin)
                .WithMany()
                .HasForeignKey(e => e.OrganizationGstinId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.InitiatedBy)
                .WithMany()
                .HasForeignKey(e => e.InitiatedById)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GstnSyncLog>(entity =>
        {
            entity.HasIndex(e => e.GstnConnectionId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartedAt);
            entity.HasIndex(e => e.CreatedAt);

            // JSON columns
            entity.Property(e => e.ImportedNoticeIds)
                .HasColumnType("jsonb");
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");

            entity.HasOne(e => e.GstnConnection)
                .WithMany(c => c.SyncLogs)
                .HasForeignKey(e => e.GstnConnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.TriggeredBy)
                .WithMany()
                .HasForeignKey(e => e.TriggeredById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ============================================================================
        // WhatsApp Bot Entity Configurations
        // ============================================================================

        // WhatsApp Verification Configuration
        modelBuilder.Entity<WhatsAppVerification>(entity =>
        {
            // Soft delete filter
            entity.HasQueryFilter(v => v.DeletedAt == null);

            // Performance indexes
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.PhoneNumber);
            entity.HasIndex(e => new { e.PhoneNumber, e.IsVerified, e.DeletedAt })
                .HasDatabaseName("IX_WhatsAppVerifications_Phone_Active");
            entity.HasIndex(e => e.ExpiresAt)
                .HasFilter("\"IsVerified\" = false AND \"DeletedAt\" IS NULL");

            // Relationship
            entity.HasOne(e => e.User)
                .WithMany(u => u.WhatsAppVerifications)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // WhatsApp Session Configuration
        modelBuilder.Entity<WhatsAppSession>(entity =>
        {
            // Soft delete filter
            entity.HasQueryFilter(s => s.DeletedAt == null);

            // Unique active session per phone number
            entity.HasIndex(e => e.PhoneNumber)
                .HasFilter("\"DeletedAt\" IS NULL")
                .IsUnique()
                .HasDatabaseName("IX_WhatsAppSessions_Phone_Unique");

            // Performance indexes
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.SessionExpiresAt)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.LastInteractionAt);

            // JSON columns
            entity.Property(e => e.Context)
                .HasColumnType("jsonb");

            // Relationships
            entity.HasOne(e => e.User)
                .WithMany(u => u.WhatsAppSessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Note: PendingVerificationId is intentionally not a FK
            // to avoid circular dependency issues during verification flow
        });

        // WhatsApp Message Log Configuration
        modelBuilder.Entity<WhatsAppMessageLog>(entity =>
        {
            // Soft delete filter
            entity.HasQueryFilter(l => l.DeletedAt == null);

            // Unique WAM ID (Meta's message ID)
            entity.HasIndex(e => e.WamId)
                .IsUnique()
                .HasFilter("\"WamId\" IS NOT NULL AND \"WamId\" != ''")
                .HasDatabaseName("IX_WhatsAppMessageLogs_WamId_Unique");

            // Performance indexes
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OrganizationId);
            entity.HasIndex(e => e.PhoneNumber);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.RetryCount, e.CreatedAt })
                .HasFilter("\"Direction\" = 'outbound' AND \"Status\" = 'failed'")
                .HasDatabaseName("IX_WhatsAppMessageLogs_FailedRetry");

            // Relationships
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // WhatsApp Template Configuration
        modelBuilder.Entity<WhatsAppTemplate>(entity =>
        {
            // Soft delete filter
            entity.HasQueryFilter(t => t.DeletedAt == null);

            // Unique template name per language
            entity.HasIndex(e => new { e.Name, e.Language })
                .IsUnique()
                .HasDatabaseName("IX_WhatsAppTemplates_Name_Language_Unique");

            // Performance indexes
            entity.HasIndex(e => e.TemplateId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.IsActive)
                .HasFilter("\"IsActive\" = true");

            // JSON columns
            entity.Property(e => e.Variables)
                .HasColumnType("jsonb");
            entity.Property(e => e.Buttons)
                .HasColumnType("jsonb");
        });

        // ============================================================================
        // REPORTING ENTITIES (GAP-RPT-006)
        // ============================================================================

        // SavedReport Configuration
        modelBuilder.Entity<SavedReport>(entity =>
        {
            // Soft delete and tenant filtering
            entity.HasQueryFilter(r =>
                r.DeletedAt == null &&
                (BypassTenantFilter || CurrentOrganizationId == null || r.OrganizationId == CurrentOrganizationId));

            // Indexes
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CreatedById)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.ReportType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.IsPublic)
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsPublic\" = true");

            // Store Configuration as JSONB
            entity.Property(e => e.Configuration)
                .HasColumnType("jsonb");

            // Relationships
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ReportSchedule Configuration
        modelBuilder.Entity<ReportSchedule>(entity =>
        {
            // Soft delete filter
            entity.HasQueryFilter(s => s.DeletedAt == null);

            // Indexes for scheduled job queries
            entity.HasIndex(e => new { e.IsActive, e.NextRunAt })
                .HasFilter("\"DeletedAt\" IS NULL AND \"IsActive\" = true AND \"NextRunAt\" IS NOT NULL")
                .HasDatabaseName("IX_ReportSchedules_Active_NextRun");

            entity.HasIndex(e => e.SavedReportId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CreatedById)
                .HasFilter("\"DeletedAt\" IS NULL");

            // Store Recipients list as JSONB
            entity.Property(e => e.Recipients)
                .HasColumnType("jsonb");

            // Relationships
            entity.HasOne(e => e.SavedReport)
                .WithMany(r => r.Schedules)
                .HasForeignKey(e => e.SavedReportId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ============================================================================
        // GST Sync Module Entity Configurations (Isolated Module)
        // ============================================================================

        // GstClient Configuration
        modelBuilder.Entity<GstClient>(entity =>
        {
            entity.ToTable("gst_clients");

            // Soft delete and tenant-scoped query filter
            entity.HasQueryFilter(c =>
                c.DeletedAt == null &&
                (BypassTenantFilter || CurrentOrganizationId == null || c.OrganizationId == CurrentOrganizationId));

            // Unique GSTIN per organization
            entity.HasIndex(e => new { e.OrganizationId, e.Gstin })
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_GstClients_Org_Gstin_Unique");

            // Performance indexes
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Gstin)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" = 'active'")
                .HasDatabaseName("IX_GstClients_Active");
            entity.HasIndex(e => e.NextSyncDueAt)
                .HasFilter("\"DeletedAt\" IS NULL AND \"SyncEnabled\" = true AND \"Status\" = 'active'")
                .HasDatabaseName("IX_GstClients_NextSyncDue");

            // Relationships
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // GstSyncSession Configuration
        modelBuilder.Entity<GstSyncSession>(entity =>
        {
            entity.ToTable("gst_sync_sessions");

            // Soft delete filter
            entity.HasQueryFilter(s => s.DeletedAt == null);

            // Performance indexes
            entity.HasIndex(e => e.GstClientId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.StartedAt)
                .IsDescending()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_GstSyncSessions_StartedAt_Desc");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL");

            // JSON column for source metadata
            entity.Property(e => e.SourceMetadata)
                .HasColumnType("jsonb");

            // Relationships
            entity.HasOne(e => e.GstClient)
                .WithMany(c => c.SyncSessions)
                .HasForeignKey(e => e.GstClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // GstNoticeRaw Configuration
        modelBuilder.Entity<GstNoticeRaw>(entity =>
        {
            entity.ToTable("gst_notices_raw");

            // Soft delete and tenant-scoped query filter
            entity.HasQueryFilter(n =>
                n.DeletedAt == null &&
                (BypassTenantFilter || CurrentOrganizationId == null || n.OrganizationId == CurrentOrganizationId));

            // Unique portal notice ID per connection
            entity.HasIndex(e => new { e.GstClientId, e.PortalNoticeId })
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_GstNoticesRaw_Client_PortalId_Unique");

            // Performance indexes
            entity.HasIndex(e => e.GstClientId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Gstin)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.NoticeType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.DueDate)
                .HasFilter("\"DeletedAt\" IS NULL AND \"DueDate\" IS NOT NULL")
                .HasDatabaseName("IX_GstNoticesRaw_DueDate");
            entity.HasIndex(e => e.ImportedToNotices)
                .HasFilter("\"DeletedAt\" IS NULL AND \"ImportedToNotices\" = false")
                .HasDatabaseName("IX_GstNoticesRaw_NotImported");
            entity.HasIndex(e => e.PortalNoticeId)
                .HasFilter("\"DeletedAt\" IS NULL");

            // JSON column for raw data
            entity.Property(e => e.RawData)
                .HasColumnType("jsonb");

            // Relationships
            entity.HasOne(e => e.GstClient)
                .WithMany(c => c.Notices)
                .HasForeignKey(e => e.GstClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.LastSyncSession)
                .WithMany(s => s.Notices)
                .HasForeignKey(e => e.LastSyncSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // GstExtensionEvent Configuration
        modelBuilder.Entity<GstExtensionEvent>(entity =>
        {
            entity.ToTable("gst_extension_events");

            // Soft delete filter
            entity.HasQueryFilter(e => e.DeletedAt == null);

            // Performance indexes
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.EventType)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.CreatedAt)
                .IsDescending()
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_GstExtensionEvents_CreatedAt_Desc");

            // JSON column for event data
            entity.Property(e => e.EventData)
                .HasColumnType("jsonb");

            // Relationships
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // GstSyncReminder Configuration
        modelBuilder.Entity<GstSyncReminder>(entity =>
        {
            entity.ToTable("gst_sync_reminders");

            // Soft delete filter
            entity.HasQueryFilter(r => r.DeletedAt == null);

            // Performance indexes
            entity.HasIndex(e => e.GstClientId)
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.Status)
                .HasFilter("\"DeletedAt\" IS NULL AND \"Status\" = 'pending'")
                .HasDatabaseName("IX_GstSyncReminders_Pending");
            entity.HasIndex(e => e.OrganizationId)
                .HasFilter("\"DeletedAt\" IS NULL");

            // Relationships
            entity.HasOne(e => e.GstClient)
                .WithMany(c => c.Reminders)
                .HasForeignKey(e => e.GstClientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Seed initial data
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Use a fixed date for seed data to avoid migration changes
        var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Seed Plans
        modelBuilder.Entity<Plan>().HasData(
            new Plan
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Free",
                Code = "free",
                PriceMonthly = 0,
                PriceYearly = 0,
                NoticeLimit = 3,
                UserLimit = 1,
                GstinLimit = 1,
                StorageLimitGb = 1,
                IsActive = true,
                CreatedAt = seedDate
            },
            new Plan
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Starter",
                Code = "starter",
                PriceMonthly = 499,
                PriceYearly = 4999,
                NoticeLimit = 10,
                UserLimit = 2,
                GstinLimit = 1,
                StorageLimitGb = 5,
                IsActive = true,
                CreatedAt = seedDate
            },
            new Plan
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Growth",
                Code = "growth",
                PriceMonthly = 999,
                PriceYearly = 9999,
                NoticeLimit = 30,
                UserLimit = 5,
                GstinLimit = 3,
                StorageLimitGb = 20,
                IsActive = true,
                CreatedAt = seedDate
            },
            new Plan
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Professional",
                Code = "professional",
                PriceMonthly = 4999,
                PriceYearly = 49999,
                NoticeLimit = 150,
                UserLimit = null, // Unlimited
                GstinLimit = null, // Unlimited
                StorageLimitGb = 100,
                IsActive = true,
                CreatedAt = seedDate
            }
        );

        // Seed GSTIN State Codes
        modelBuilder.Entity<GstinStateCode>().HasData(
            new GstinStateCode { Code = "01", Name = "Jammu and Kashmir", IsUnionTerritory = true },
            new GstinStateCode { Code = "02", Name = "Himachal Pradesh", IsUnionTerritory = false },
            new GstinStateCode { Code = "03", Name = "Punjab", IsUnionTerritory = false },
            new GstinStateCode { Code = "04", Name = "Chandigarh", IsUnionTerritory = true },
            new GstinStateCode { Code = "05", Name = "Uttarakhand", IsUnionTerritory = false },
            new GstinStateCode { Code = "06", Name = "Haryana", IsUnionTerritory = false },
            new GstinStateCode { Code = "07", Name = "Delhi", IsUnionTerritory = true },
            new GstinStateCode { Code = "08", Name = "Rajasthan", IsUnionTerritory = false },
            new GstinStateCode { Code = "09", Name = "Uttar Pradesh", IsUnionTerritory = false },
            new GstinStateCode { Code = "10", Name = "Bihar", IsUnionTerritory = false },
            new GstinStateCode { Code = "11", Name = "Sikkim", IsUnionTerritory = false },
            new GstinStateCode { Code = "12", Name = "Arunachal Pradesh", IsUnionTerritory = false },
            new GstinStateCode { Code = "13", Name = "Nagaland", IsUnionTerritory = false },
            new GstinStateCode { Code = "14", Name = "Manipur", IsUnionTerritory = false },
            new GstinStateCode { Code = "15", Name = "Mizoram", IsUnionTerritory = false },
            new GstinStateCode { Code = "16", Name = "Tripura", IsUnionTerritory = false },
            new GstinStateCode { Code = "17", Name = "Meghalaya", IsUnionTerritory = false },
            new GstinStateCode { Code = "18", Name = "Assam", IsUnionTerritory = false },
            new GstinStateCode { Code = "19", Name = "West Bengal", IsUnionTerritory = false },
            new GstinStateCode { Code = "20", Name = "Jharkhand", IsUnionTerritory = false },
            new GstinStateCode { Code = "21", Name = "Odisha", IsUnionTerritory = false },
            new GstinStateCode { Code = "22", Name = "Chhattisgarh", IsUnionTerritory = false },
            new GstinStateCode { Code = "23", Name = "Madhya Pradesh", IsUnionTerritory = false },
            new GstinStateCode { Code = "24", Name = "Gujarat", IsUnionTerritory = false },
            new GstinStateCode { Code = "26", Name = "Dadra and Nagar Haveli and Daman and Diu", IsUnionTerritory = true },
            new GstinStateCode { Code = "27", Name = "Maharashtra", IsUnionTerritory = false },
            new GstinStateCode { Code = "28", Name = "Andhra Pradesh (Old)", IsUnionTerritory = false },
            new GstinStateCode { Code = "29", Name = "Karnataka", IsUnionTerritory = false },
            new GstinStateCode { Code = "30", Name = "Goa", IsUnionTerritory = false },
            new GstinStateCode { Code = "31", Name = "Lakshadweep", IsUnionTerritory = true },
            new GstinStateCode { Code = "32", Name = "Kerala", IsUnionTerritory = false },
            new GstinStateCode { Code = "33", Name = "Tamil Nadu", IsUnionTerritory = false },
            new GstinStateCode { Code = "34", Name = "Puducherry", IsUnionTerritory = true },
            new GstinStateCode { Code = "35", Name = "Andaman and Nicobar Islands", IsUnionTerritory = true },
            new GstinStateCode { Code = "36", Name = "Telangana", IsUnionTerritory = false },
            new GstinStateCode { Code = "37", Name = "Andhra Pradesh", IsUnionTerritory = false },
            new GstinStateCode { Code = "38", Name = "Ladakh", IsUnionTerritory = true },
            new GstinStateCode { Code = "97", Name = "Other Territory", IsUnionTerritory = false },
            new GstinStateCode { Code = "99", Name = "Centre Jurisdiction", IsUnionTerritory = false }
        );

        // Note: SubscriptionPlan seeding is done via SQL migration because
        // EF Core HasData doesn't support owned JSON types (Limits property).
        // See migration "SeedSubscriptionPlans" for the seed data.
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is BaseEntity && (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entry in entries)
        {
            var entity = (BaseEntity)entry.Entity;

            if (entry.State == EntityState.Added)
            {
                entity.CreatedAt = DateTime.UtcNow;
            }

            entity.UpdatedAt = DateTime.UtcNow;
        }
    }
}
