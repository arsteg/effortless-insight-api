using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Data;

/// <summary>
/// Seeds the default workflow template and stages on application startup.
/// </summary>
public class WorkflowTemplateSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<WorkflowTemplateSeeder> _logger;

    // Fixed IDs for default template and stages (for consistency across environments)
    public static readonly Guid DefaultTemplateId = Guid.Parse("00000000-0000-0000-0001-000000000001");
    public static readonly Guid IntakeStageId = Guid.Parse("00000000-0000-0000-0002-000000000001");
    public static readonly Guid AnalysisStageId = Guid.Parse("00000000-0000-0000-0002-000000000002");
    public static readonly Guid DraftingStageId = Guid.Parse("00000000-0000-0000-0002-000000000003");
    public static readonly Guid ReviewStageId = Guid.Parse("00000000-0000-0000-0002-000000000004");
    public static readonly Guid ApprovalStageId = Guid.Parse("00000000-0000-0000-0002-000000000005");
    public static readonly Guid SubmissionStageId = Guid.Parse("00000000-0000-0000-0002-000000000006");
    public static readonly Guid AwaitingStageId = Guid.Parse("00000000-0000-0000-0002-000000000007");
    public static readonly Guid ClosedStageId = Guid.Parse("00000000-0000-0000-0002-000000000008");

    public WorkflowTemplateSeeder(ApplicationDbContext context, ILogger<WorkflowTemplateSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existingTemplate = await _context.WorkflowTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == DefaultTemplateId, cancellationToken);

        if (existingTemplate != null)
        {
            _logger.LogInformation("Default workflow template already exists, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding default workflow template...");

        var now = DateTime.UtcNow;

        var template = new WorkflowTemplate
        {
            Id = DefaultTemplateId,
            OrganizationId = null, // System template
            Name = "Standard Notice Workflow",
            Description = "Default 8-stage workflow for GST notice processing with SLA tracking and escalation support.",
            Version = 1,
            IsSystem = true,
            IsActive = true,
            ApplicableNoticeTypes = ["*"],
            CreatedAt = now,
            UpdatedAt = now
        };

        var stages = CreateDefaultStages(now);

        _context.WorkflowTemplates.Add(template);
        _context.WorkflowStages.AddRange(stages);

        // Add default escalation rules
        var escalationRules = CreateDefaultEscalationRules(now);
        _context.WorkflowEscalationRules.AddRange(escalationRules);

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Default workflow template seeded successfully with {StageCount} stages and {RuleCount} escalation rules",
            stages.Count, escalationRules.Count);
    }

    private static List<WorkflowStage> CreateDefaultStages(DateTime now)
    {
        return
        [
            // Stage 1: Intake
            new WorkflowStage
            {
                Id = IntakeStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "intake",
                Name = "Intake",
                Description = "Initial receipt and classification of the notice. Document validation and basic data extraction.",
                StageType = WorkflowStageTypes.Start,
                StageOrder = 1,
                SlaHours = 4,
                SlaWarningPercent = 75,
                Color = "#3B82F6", // Blue
                Icon = "inbox",
                AllowedTransitions = ["analysis", "closed"],
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "triggerAI",
                        Config = new Dictionary<string, object>
                        {
                            ["operation"] = "extract_metadata",
                            ["priority"] = "high"
                        }
                    },
                    new WorkflowAction
                    {
                        Type = "notify",
                        Config = new Dictionary<string, object>
                        {
                            ["target"] = "assignee",
                            ["template"] = "notice_assigned"
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules =
                [
                    new AutoTransitionRule
                    {
                        Condition = new WorkflowCondition
                        {
                            Field = "aiAnalysisComplete",
                            Operator = "eq",
                            Value = true
                        },
                        TargetStage = "analysis",
                        DelayMinutes = 0
                    }
                ],
                Metadata = new Dictionary<string, object>
                {
                    ["requiresManualReview"] = false
                },
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 2: Analysis
            new WorkflowStage
            {
                Id = AnalysisStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "analysis",
                Name = "Analysis",
                Description = "Detailed review of notice content, risk assessment, and determination of response strategy.",
                StageType = WorkflowStageTypes.Intermediate,
                StageOrder = 2,
                SlaHours = 24,
                SlaWarningPercent = 75,
                Color = "#8B5CF6", // Purple
                Icon = "search",
                AllowedTransitions = ["drafting", "intake", "closed"],
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "triggerAI",
                        Config = new Dictionary<string, object>
                        {
                            ["operation"] = "risk_assessment",
                            ["includeRecommendations"] = true
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules = [],
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 3: Drafting
            new WorkflowStage
            {
                Id = DraftingStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "drafting",
                Name = "Drafting",
                Description = "Preparation of response documents, supporting evidence, and compliance materials.",
                StageType = WorkflowStageTypes.Intermediate,
                StageOrder = 3,
                SlaHours = 48,
                SlaWarningPercent = 75,
                Color = "#F59E0B", // Amber
                Icon = "edit",
                AllowedTransitions = ["review", "analysis", "closed"],
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "triggerAI",
                        Config = new Dictionary<string, object>
                        {
                            ["operation"] = "generate_draft",
                            ["useTemplate"] = true
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules = [],
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 4: Review
            new WorkflowStage
            {
                Id = ReviewStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "review",
                Name = "Review",
                Description = "Quality assurance review of prepared response by senior team member or subject matter expert.",
                StageType = WorkflowStageTypes.Intermediate,
                StageOrder = 4,
                SlaHours = 24,
                SlaWarningPercent = 75,
                Color = "#EC4899", // Pink
                Icon = "eye",
                AllowedTransitions = ["approval", "drafting", "closed"],
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "notify",
                        Config = new Dictionary<string, object>
                        {
                            ["target"] = "reviewer",
                            ["template"] = "review_required"
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules = [],
                Metadata = new Dictionary<string, object>
                {
                    ["requiresApproval"] = true,
                    ["minReviewerRole"] = "senior_member"
                },
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 5: Approval
            new WorkflowStage
            {
                Id = ApprovalStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "approval",
                Name = "Approval",
                Description = "Final approval by authorized personnel before submission to tax authority.",
                StageType = WorkflowStageTypes.Intermediate,
                StageOrder = 5,
                SlaHours = 8,
                SlaWarningPercent = 75,
                Color = "#10B981", // Emerald
                Icon = "check-circle",
                AllowedTransitions = ["submission", "review", "closed"],
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "notify",
                        Config = new Dictionary<string, object>
                        {
                            ["target"] = "approver",
                            ["template"] = "approval_required"
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules = [],
                Metadata = new Dictionary<string, object>
                {
                    ["requiresApproval"] = true,
                    ["minApproverRole"] = "admin"
                },
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 6: Submission
            new WorkflowStage
            {
                Id = SubmissionStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "submission",
                Name = "Submission",
                Description = "Filing of response with tax authority through GST portal or physical submission.",
                StageType = WorkflowStageTypes.Intermediate,
                StageOrder = 6,
                SlaHours = 4,
                SlaWarningPercent = 75,
                Color = "#06B6D4", // Cyan
                Icon = "send",
                AllowedTransitions = ["awaiting", "approval", "closed"],
                EntryActions = [],
                ExitActions =
                [
                    new WorkflowAction
                    {
                        Type = "logSubmission",
                        Config = new Dictionary<string, object>
                        {
                            ["captureAcknowledgement"] = true
                        }
                    }
                ],
                AutoTransitionRules = [],
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 7: Awaiting Response
            new WorkflowStage
            {
                Id = AwaitingStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "awaiting",
                Name = "Awaiting Response",
                Description = "Waiting for response or acknowledgement from tax authority after submission.",
                StageType = WorkflowStageTypes.Pause,
                StageOrder = 7,
                SlaHours = null, // No SLA for waiting stage
                SlaWarningPercent = 75,
                Color = "#6B7280", // Gray
                Icon = "clock",
                AllowedTransitions = ["closed", "analysis"],
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "setField",
                        Config = new Dictionary<string, object>
                        {
                            ["field"] = "awaitingResponseSince",
                            ["value"] = "{{now}}"
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules = [],
                CreatedAt = now,
                UpdatedAt = now
            },

            // Stage 8: Closed
            new WorkflowStage
            {
                Id = ClosedStageId,
                WorkflowTemplateId = DefaultTemplateId,
                StageKey = "closed",
                Name = "Closed",
                Description = "Notice has been resolved, whether through successful response, withdrawal, or other resolution.",
                StageType = WorkflowStageTypes.End,
                StageOrder = 8,
                SlaHours = null, // No SLA for end stage
                SlaWarningPercent = 75,
                Color = "#22C55E", // Green
                Icon = "check",
                AllowedTransitions = [], // Cannot transition out of closed
                EntryActions =
                [
                    new WorkflowAction
                    {
                        Type = "updateMetrics",
                        Config = new Dictionary<string, object>
                        {
                            ["captureCompletionTime"] = true,
                            ["calculateSlaCompliance"] = true
                        }
                    },
                    new WorkflowAction
                    {
                        Type = "notify",
                        Config = new Dictionary<string, object>
                        {
                            ["target"] = "assignee",
                            ["template"] = "notice_closed"
                        }
                    }
                ],
                ExitActions = [],
                AutoTransitionRules = [],
                Metadata = new Dictionary<string, object>
                {
                    ["isFinalStage"] = true,
                    ["allowReopen"] = false
                },
                CreatedAt = now,
                UpdatedAt = now
            }
        ];
    }

    private static List<WorkflowEscalationRule> CreateDefaultEscalationRules(DateTime now)
    {
        return
        [
            new WorkflowEscalationRule
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000001"),
                WorkflowTemplateId = DefaultTemplateId,
                Name = "SLA Warning - 75%",
                TriggerPercent = 75,
                Actions =
                [
                    new EscalationAction
                    {
                        Type = "notify",
                        Target = "assignee",
                        Template = "sla_warning"
                    },
                    new EscalationAction
                    {
                        Type = "flag",
                        Value = "sla_warning"
                    }
                ],
                CreatedAt = now,
                UpdatedAt = now
            },
            new WorkflowEscalationRule
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000002"),
                WorkflowTemplateId = DefaultTemplateId,
                Name = "SLA At Risk - 90%",
                TriggerPercent = 90,
                Actions =
                [
                    new EscalationAction
                    {
                        Type = "notify",
                        Target = "assignee",
                        Template = "sla_at_risk"
                    },
                    new EscalationAction
                    {
                        Type = "notify",
                        Target = "manager",
                        Template = "sla_escalation"
                    },
                    new EscalationAction
                    {
                        Type = "flag",
                        Value = "sla_at_risk"
                    }
                ],
                CreatedAt = now,
                UpdatedAt = now
            },
            new WorkflowEscalationRule
            {
                Id = Guid.Parse("00000000-0000-0000-0003-000000000003"),
                WorkflowTemplateId = DefaultTemplateId,
                Name = "SLA Breach - 100%",
                TriggerPercent = 100,
                Actions =
                [
                    new EscalationAction
                    {
                        Type = "notify",
                        Target = "assignee",
                        Template = "sla_breach"
                    },
                    new EscalationAction
                    {
                        Type = "notify",
                        Target = "manager",
                        Template = "sla_breach"
                    },
                    new EscalationAction
                    {
                        Type = "notify",
                        Target = "admin",
                        Template = "sla_breach_critical"
                    },
                    new EscalationAction
                    {
                        Type = "flag",
                        Value = "sla_breached"
                    }
                ],
                CreatedAt = now,
                UpdatedAt = now
            }
        ];
    }
}
