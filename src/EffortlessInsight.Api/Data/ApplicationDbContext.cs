using EffortlessInsight.Api.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace EffortlessInsight.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<OrganizationGstin> OrganizationGstins => Set<OrganizationGstin>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();
    public DbSet<GstinStateCode> GstinStateCodes => Set<GstinStateCode>();
    public DbSet<Notice> Notices => Set<Notice>();
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

    // Workflow Engine entities
    public DbSet<WorkflowTemplate> WorkflowTemplates => Set<WorkflowTemplate>();
    public DbSet<WorkflowStage> WorkflowStages => Set<WorkflowStage>();
    public DbSet<WorkflowAssignmentRule> WorkflowAssignmentRules => Set<WorkflowAssignmentRule>();
    public DbSet<WorkflowEscalationRule> WorkflowEscalationRules => Set<WorkflowEscalationRule>();
    public DbSet<NoticeWorkflowInstance> NoticeWorkflowInstances => Set<NoticeWorkflowInstance>();
    public DbSet<WorkflowHistory> WorkflowHistories => Set<WorkflowHistory>();
    public DbSet<NoticeDeadline> NoticeDeadlines => Set<NoticeDeadline>();
    public DbSet<DeadlineExtension> DeadlineExtensions => Set<DeadlineExtension>();
    public DbSet<WorkflowSlaMetric> WorkflowSlaMetrics => Set<WorkflowSlaMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Configure entities
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filter for soft delete
        modelBuilder.Entity<Organization>().HasQueryFilter(o => o.DeletedAt == null);
        modelBuilder.Entity<ApplicationUser>().HasQueryFilter(u => u.DeletedAt == null);
        modelBuilder.Entity<Notice>().HasQueryFilter(n => n.DeletedAt == null);
        modelBuilder.Entity<Comment>().HasQueryFilter(c => c.DeletedAt == null);
        modelBuilder.Entity<NoticeResponse>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<DeadlineReminder>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<NoticeTask>().HasQueryFilter(t => t.DeletedAt == null);
        modelBuilder.Entity<Attachment>().HasQueryFilter(a => a.DeletedAt == null);
        modelBuilder.Entity<WorkflowTemplate>().HasQueryFilter(w => w.DeletedAt == null);
        modelBuilder.Entity<WorkflowStage>().HasQueryFilter(s => s.DeletedAt == null);
        modelBuilder.Entity<WorkflowAssignmentRule>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<WorkflowEscalationRule>().HasQueryFilter(r => r.DeletedAt == null);
        modelBuilder.Entity<NoticeWorkflowInstance>().HasQueryFilter(i => i.DeletedAt == null);
        modelBuilder.Entity<WorkflowHistory>().HasQueryFilter(h => h.DeletedAt == null);
        modelBuilder.Entity<NoticeDeadline>().HasQueryFilter(d => d.DeletedAt == null);
        modelBuilder.Entity<DeadlineExtension>().HasQueryFilter(e => e.DeletedAt == null);
        modelBuilder.Entity<WorkflowSlaMetric>().HasQueryFilter(m => m.DeletedAt == null);

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

        modelBuilder.Entity<WorkflowSlaMetric>()
            .Property(m => m.AssigneeBreakdown)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowSlaMetric>()
            .Property(m => m.NoticeTypeBreakdown)
            .HasColumnType("jsonb");
        modelBuilder.Entity<WorkflowSlaMetric>()
            .Property(m => m.PriorityBreakdown)
            .HasColumnType("jsonb");

        // Configure vector column
        modelBuilder.Entity<Embedding>()
            .Property(e => e.Vector)
            .HasColumnType("vector(3072)");

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
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_OrganizationGstins_Gstin_Format",
                "\"Gstin\" ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[A-Z0-9]{1}Z[A-Z0-9]{1}$'"
            ));

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

            // Composite index for common queries
            entity.HasIndex(n => new { n.OrganizationId, n.Status, n.ResponseDeadline })
                .HasFilter("\"DeletedAt\" IS NULL")
                .HasDatabaseName("IX_Notices_Org_Status_Deadline");

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
