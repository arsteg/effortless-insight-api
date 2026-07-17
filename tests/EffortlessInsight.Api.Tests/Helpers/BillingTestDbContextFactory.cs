using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Data.Entities.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace EffortlessInsight.Api.Tests.Helpers;

/// <summary>
/// Creates a properly configured DbContext for billing tests using InMemory provider.
/// </summary>
public static class BillingTestDbContextFactory
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var context = new TestableApplicationDbContext(options);
        context.Database.EnsureCreated();

        // Clear seeded subscription plans to allow tests to control their data
        context.SubscriptionPlans.RemoveRange(context.SubscriptionPlans);
        context.SaveChanges();

        return context;
    }
}

/// <summary>
/// A test-specific ApplicationDbContext that configures entities for InMemory provider compatibility.
/// Inherits from ApplicationDbContext so it can be used with all billing services.
/// </summary>
public class TestableApplicationDbContext : ApplicationDbContext
{
    public TestableApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Call base to get all entity configurations
        base.OnModelCreating(modelBuilder);

        // Create JSON converters for Dictionary<string, object>
        var jsonConverter = new ValueConverter<Dictionary<string, object>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
        );

        var jsonConverterNullable = new ValueConverter<Dictionary<string, object>?, string>(
            v => v == null ? "{}" : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null)
        );

        // Fix ActivityLog.Data for InMemory
        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.Property(e => e.Data).HasConversion(jsonConverter);
        });

        // Fix ApplicationUser.Preferences for InMemory
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(e => e.Preferences).HasConversion(jsonConverterNullable);
            entity.Property(e => e.BackupCodesHash).HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null)
            );
        });

        // Note: SubscriptionPlan.Limits is configured as OwnsOne().ToJson() in base context.
        // We need to configure Metadata and Features for InMemory compatibility.
        var featureListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );

        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
            entity.Property(e => e.Features).HasConversion(featureListConverter);
        });

        // Fix Coupon.Metadata for InMemory
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix Payment.Metadata for InMemory
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix Notice.Metadata for InMemory
        modelBuilder.Entity<Notice>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix NotificationTemplate.Metadata for InMemory
        modelBuilder.Entity<NotificationTemplate>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverter);
        });

        // Fix Notification.Data for InMemory
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(e => e.Data).HasConversion(jsonConverter);
        });

        // Fix NotificationDelivery.Metadata for InMemory
        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverter);
        });

        // Fix AuditLog for InMemory
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(e => e.OldValues).HasConversion(jsonConverterNullable);
            entity.Property(e => e.NewValues).HasConversion(jsonConverterNullable);
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix UserNotificationPreference for InMemory
        modelBuilder.Entity<UserNotificationPreference>(entity =>
        {
            entity.Property(e => e.ChannelSettings).HasConversion(jsonConverter);
            entity.Property(e => e.QuietHours).HasConversion(jsonConverter);
            entity.Property(e => e.TypePreferences).HasConversion(jsonConverter);
            entity.Property(e => e.DigestSettings).HasConversion(jsonConverter);
        });

        // Fix PushToken.DeviceInfo for InMemory
        modelBuilder.Entity<PushToken>(entity =>
        {
            entity.Property(e => e.DeviceInfo).HasConversion(jsonConverter);
        });

        // Fix Organization.Settings for InMemory
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(e => e.Settings).HasConversion(jsonConverterNullable);
        });

        // Fix OrganizationMember.NotificationPreferences for InMemory
        modelBuilder.Entity<OrganizationMember>(entity =>
        {
            entity.Property(e => e.NotificationPreferences).HasConversion(jsonConverterNullable);
        });

        // Fix Embedding.Metadata and Vector for InMemory
        modelBuilder.Entity<Embedding>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
            // Ignore Vector for InMemory testing as it's a pgvector-specific type
            entity.Ignore(e => e.Vector);
        });

        // Fix KnowledgeBase.Metadata for InMemory
        modelBuilder.Entity<KnowledgeBase>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix DeadlineExtension.Metadata for InMemory
        modelBuilder.Entity<DeadlineExtension>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix NoticeDeadline.Metadata for InMemory
        modelBuilder.Entity<NoticeDeadline>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix NoticeAiReport for InMemory
        modelBuilder.Entity<NoticeAiReport>(entity =>
        {
            entity.Property(e => e.ActionItems).HasConversion(jsonConverterNullable);
            entity.Property(e => e.RequiredDocuments).HasConversion(jsonConverterNullable);
            entity.Property(e => e.LegalReferences).HasConversion(jsonConverterNullable);
            entity.Property(e => e.ConfidenceScores).HasConversion(jsonConverterNullable);
            entity.Property(e => e.FullReportJson).HasConversion(jsonConverterNullable);
        });

        // Fix NoticeWorkflowInstance.Metadata for InMemory
        modelBuilder.Entity<NoticeWorkflowInstance>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix WorkflowHistory.EventData for InMemory
        modelBuilder.Entity<WorkflowHistory>(entity =>
        {
            entity.Property(e => e.EventData).HasConversion(jsonConverterNullable);
        });

        // Fix WorkflowStage.Metadata for InMemory
        modelBuilder.Entity<WorkflowStage>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix WorkflowAction.Config for InMemory
        modelBuilder.Entity<WorkflowAction>(entity =>
        {
            entity.Property(e => e.Config).HasConversion(jsonConverter);
        });

        // Fix NoticeFile.Metadata for InMemory
        modelBuilder.Entity<NoticeFile>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix BillingSubscription.Metadata for InMemory
        modelBuilder.Entity<BillingSubscription>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix InvoiceLineItem.Metadata for InMemory
        modelBuilder.Entity<InvoiceLineItem>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix WebhookEvent.ProcessingResult for InMemory
        modelBuilder.Entity<WebhookEvent>(entity =>
        {
            entity.Property(e => e.ProcessingResult).HasConversion(jsonConverterNullable);
        });

        // Fix UsageRecord.AdditionalMetrics for InMemory
        modelBuilder.Entity<UsageRecord>(entity =>
        {
            entity.Property(e => e.AdditionalMetrics).HasConversion(jsonConverterNullable);
        });

        // Fix PaymentMethod.Metadata for InMemory
        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix Invoice.BillingDetails (owned entity) for InMemory
        var billingDetailsConverter = new ValueConverter<InvoiceBillingDetails, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<InvoiceBillingDetails>(v, (JsonSerializerOptions?)null) ?? new InvoiceBillingDetails()
        );

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.Property(e => e.BillingDetails).HasConversion(billingDetailsConverter);
        });

        // Fix ScheduledNotification.Data for InMemory
        modelBuilder.Entity<ScheduledNotification>(entity =>
        {
            entity.Property(e => e.Data).HasConversion(jsonConverter);
        });

        // Fix WorkflowAssignmentRule.Conditions and Actions for InMemory
        var ruleConditionsConverter = new ValueConverter<List<RuleCondition>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<RuleCondition>>(v, (JsonSerializerOptions?)null) ?? new List<RuleCondition>()
        );

        var ruleActionsConverter = new ValueConverter<List<RuleAction>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<RuleAction>>(v, (JsonSerializerOptions?)null) ?? new List<RuleAction>()
        );

        modelBuilder.Entity<WorkflowAssignmentRule>(entity =>
        {
            entity.Property(e => e.Conditions).HasConversion(ruleConditionsConverter);
            entity.Property(e => e.Actions).HasConversion(ruleActionsConverter);
        });

        // Fix WorkflowEscalationRule.Actions for InMemory
        var escalationActionsConverter = new ValueConverter<List<EscalationAction>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<EscalationAction>>(v, (JsonSerializerOptions?)null) ?? new List<EscalationAction>()
        );

        modelBuilder.Entity<WorkflowEscalationRule>(entity =>
        {
            entity.Property(e => e.Actions).HasConversion(escalationActionsConverter);
        });

        // Fix WorkflowSlaMetric properties for InMemory
        var assigneeBreakdownConverter = new ValueConverter<Dictionary<string, AssigneeMetrics>?, string>(
            v => v == null ? "{}" : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, AssigneeMetrics>>(v, (JsonSerializerOptions?)null)
        );

        var noticeTypeBreakdownConverter = new ValueConverter<Dictionary<string, int>?, string>(
            v => v == null ? "{}" : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, int>>(v, (JsonSerializerOptions?)null)
        );

        modelBuilder.Entity<WorkflowSlaMetric>(entity =>
        {
            entity.Property(e => e.AssigneeBreakdown).HasConversion(assigneeBreakdownConverter);
            entity.Property(e => e.NoticeTypeBreakdown).HasConversion(noticeTypeBreakdownConverter);
            entity.Property(e => e.PriorityBreakdown).HasConversion(noticeTypeBreakdownConverter);
        });

        // Fix WorkflowStage List properties for InMemory
        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );

        var stringListNullableConverter = new ValueConverter<List<string>?, string>(
            v => v == null ? "[]" : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)
        );

        var guidListNullableConverter = new ValueConverter<List<Guid>?, string>(
            v => v == null ? "[]" : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<Guid>>(v, (JsonSerializerOptions?)null)
        );

        var intListConverter = new ValueConverter<List<int>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
        );

        var autoTransitionRulesConverter = new ValueConverter<List<AutoTransitionRule>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<AutoTransitionRule>>(v, (JsonSerializerOptions?)null) ?? new List<AutoTransitionRule>()
        );

        var workflowActionsConverter = new ValueConverter<List<WorkflowAction>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<WorkflowAction>>(v, (JsonSerializerOptions?)null) ?? new List<WorkflowAction>()
        );

        modelBuilder.Entity<WorkflowStage>(entity =>
        {
            entity.Property(e => e.AllowedTransitions).HasConversion(stringListConverter);
            entity.Property(e => e.EntryActions).HasConversion(workflowActionsConverter);
            entity.Property(e => e.ExitActions).HasConversion(workflowActionsConverter);
            entity.Property(e => e.AutoTransitionRules).HasConversion(autoTransitionRulesConverter);
        });

        // Fix Coupon List properties for InMemory
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.Property(e => e.ApplicablePlans).HasConversion(stringListConverter);
            entity.Property(e => e.ApplicableCycles).HasConversion(stringListConverter);
        });

        // Fix SubscriptionPlan.Features for InMemory
        modelBuilder.Entity<SubscriptionPlan>(entity =>
        {
            entity.Property(e => e.Features).HasConversion(stringListConverter);
        });

        // Fix Comment List properties for InMemory
        modelBuilder.Entity<Comment>(entity =>
        {
            entity.Property(e => e.Mentions).HasConversion(guidListNullableConverter);
            entity.Property(e => e.AttachmentUrls).HasConversion(stringListNullableConverter);
        });

        // Fix DeadlineExtension.SupportingDocumentIds for InMemory
        modelBuilder.Entity<DeadlineExtension>(entity =>
        {
            entity.Property(e => e.SupportingDocumentIds).HasConversion(stringListNullableConverter);
        });

        // Fix Notice.Tags for InMemory
        modelBuilder.Entity<Notice>(entity =>
        {
            entity.Property(e => e.Tags).HasConversion(stringListNullableConverter);
        });

        // Fix NoticeDeadline.ReminderDaysBefore for InMemory
        modelBuilder.Entity<NoticeDeadline>(entity =>
        {
            entity.Property(e => e.ReminderDaysBefore).HasConversion(intListConverter);
        });

        // Fix Organization.Gstins for InMemory
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(e => e.Gstins).HasConversion(stringListConverter);
        });

        // Fix TaskTemplate List properties for InMemory
        modelBuilder.Entity<TaskTemplate>(entity =>
        {
            entity.Property(e => e.DefaultLabels).HasConversion(stringListNullableConverter);
            entity.Property(e => e.ApplicableNoticeTypes).HasConversion(stringListNullableConverter);
        });

        // Fix WorkflowTemplate.ApplicableNoticeTypes for InMemory
        modelBuilder.Entity<WorkflowTemplate>(entity =>
        {
            entity.Property(e => e.ApplicableNoticeTypes).HasConversion(stringListConverter);
        });

        // Fix Admin entities for InMemory
        modelBuilder.Entity<EffortlessInsight.Api.Data.Entities.Admin.AdminAuditLog>(entity =>
        {
            entity.Property(e => e.Details).HasConversion(jsonConverter);
        });

        modelBuilder.Entity<EffortlessInsight.Api.Data.Entities.Admin.SystemAlert>(entity =>
        {
            entity.Property(e => e.Data).HasConversion(jsonConverter);
        });

        // Converter for Dictionary<string, string>
        var stringDictConverter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>()
        );

        modelBuilder.Entity<EffortlessInsight.Api.Data.Entities.Admin.PromptVersion>(entity =>
        {
            entity.Property(e => e.ModelConfig).HasConversion(jsonConverter);
            entity.Property(e => e.Variables).HasConversion(stringDictConverter);
            entity.Property(e => e.OutputSchema).HasConversion(jsonConverterNullable);
            entity.Property(e => e.TestResults).HasConversion(jsonConverterNullable);
        });

        // Fix ApprovalChain Dictionary properties for InMemory
        modelBuilder.Entity<ApprovalChain>(entity =>
        {
            entity.Property(e => e.TriggerConditions).HasConversion(jsonConverterNullable);
        });

        // Fix ApprovalStep Dictionary properties for InMemory
        modelBuilder.Entity<ApprovalStep>(entity =>
        {
            entity.Property(e => e.Conditions).HasConversion(jsonConverterNullable);
        });

        // Fix ApprovalRequest Dictionary properties for InMemory
        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix DataExport Dictionary properties for InMemory
        modelBuilder.Entity<DataExport>(entity =>
        {
            entity.Property(e => e.Options).HasConversion(jsonConverterNullable);
            entity.Property(e => e.Summary).HasConversion(jsonConverterNullable);
        });

        // Fix Team Dictionary properties for InMemory
        modelBuilder.Entity<Team>(entity =>
        {
            entity.Property(e => e.Settings).HasConversion(jsonConverterNullable);
        });

        // Fix WorkflowStageInstance Dictionary properties for InMemory
        modelBuilder.Entity<WorkflowStageInstance>(entity =>
        {
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Fix GstnSyncLog List and Dictionary properties for InMemory
        modelBuilder.Entity<GstnSyncLog>(entity =>
        {
            entity.Property(e => e.ImportedNoticeIds).HasConversion(guidListNullableConverter);
            entity.Property(e => e.Metadata).HasConversion(jsonConverterNullable);
        });

        // Ignore value object types that are serialized as JSON (not separate tables)
        modelBuilder.Ignore<WorkflowAction>();
        modelBuilder.Ignore<AutoTransitionRule>();
        modelBuilder.Ignore<RuleCondition>();
        modelBuilder.Ignore<RuleAction>();
        modelBuilder.Ignore<EscalationAction>();
        modelBuilder.Ignore<WorkflowCondition>();
        modelBuilder.Ignore<AssigneeMetrics>();
    }
}
