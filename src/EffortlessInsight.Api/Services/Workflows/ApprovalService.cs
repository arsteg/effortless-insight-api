using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Workflows;

/// <summary>
/// Service interface for managing approval chains and processing approvals.
/// </summary>
public interface IApprovalService
{
    // Chain Management
    Task<ApprovalChain> CreateChainAsync(CreateApprovalChainRequest request, Guid organizationId, CancellationToken ct = default);
    Task<ApprovalChain?> GetChainByIdAsync(Guid chainId, Guid organizationId, CancellationToken ct = default);
    Task<List<ApprovalChain>> GetChainsAsync(Guid organizationId, bool? isActive = null, CancellationToken ct = default);
    Task<ApprovalChain> UpdateChainAsync(Guid chainId, UpdateApprovalChainRequest request, Guid organizationId, CancellationToken ct = default);
    Task DeleteChainAsync(Guid chainId, Guid organizationId, CancellationToken ct = default);

    // Approval Request Processing
    Task<ApprovalRequest> SubmitForApprovalAsync(SubmitApprovalRequest request, Guid organizationId, Guid userId, CancellationToken ct = default);
    Task<ApprovalRequest?> GetRequestByIdAsync(Guid requestId, Guid organizationId, CancellationToken ct = default);
    Task<List<ApprovalRequest>> GetPendingApprovalsAsync(Guid organizationId, Guid userId, CancellationToken ct = default);
    Task<List<ApprovalRequest>> GetRequestsByNoticeAsync(Guid noticeId, Guid organizationId, CancellationToken ct = default);

    // Approval Actions
    Task<ApprovalRequest> ApproveAsync(Guid requestId, string? comments, Guid userId, string? ipAddress = null, CancellationToken ct = default);
    Task<ApprovalRequest> RejectAsync(Guid requestId, string comments, Guid userId, string? ipAddress = null, CancellationToken ct = default);
    Task<ApprovalRequest> DelegateAsync(Guid requestId, Guid delegateToUserId, string? reason, Guid userId, string? ipAddress = null, CancellationToken ct = default);
    Task<ApprovalRequest> RecallAsync(Guid requestId, Guid userId, CancellationToken ct = default);

    // Escalation
    Task ProcessEscalationsAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementation of approval service.
/// </summary>
public class ApprovalService : IApprovalService
{
    private readonly ApplicationDbContext _db;
    private readonly IAuditService _auditService;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(
        ApplicationDbContext db,
        IAuditService auditService,
        ILogger<ApprovalService> logger)
    {
        _db = db;
        _auditService = auditService;
        _logger = logger;
    }

    #region Chain Management

    public async Task<ApprovalChain> CreateChainAsync(
        CreateApprovalChainRequest request,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var chain = new ApprovalChain
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = request.Name,
            Description = request.Description,
            TriggerEvent = request.TriggerEvent,
            TriggerConditions = request.TriggerConditions,
            IsActive = request.IsActive ?? true,
            IsParallel = request.IsParallel ?? false,
            MinApprovalsRequired = request.MinApprovalsRequired,
            DefaultTimeoutHours = request.DefaultTimeoutHours,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Add steps
        if (request.Steps?.Any() == true)
        {
            var stepOrder = 1;
            foreach (var stepRequest in request.Steps)
            {
                var step = new ApprovalStep
                {
                    Id = Guid.NewGuid(),
                    ApprovalChainId = chain.Id,
                    StepOrder = stepOrder++,
                    Name = stepRequest.Name,
                    ApproverType = stepRequest.ApproverType ?? ApproverType.User,
                    ApproverId = stepRequest.ApproverId,
                    ApproverRole = stepRequest.ApproverRole,
                    IsOptional = stepRequest.IsOptional ?? false,
                    Conditions = stepRequest.Conditions,
                    TimeoutHours = stepRequest.TimeoutHours,
                    EscalationUserId = stepRequest.EscalationUserId,
                    AllowDelegation = stepRequest.AllowDelegation ?? true,
                    Instructions = stepRequest.Instructions,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                chain.Steps.Add(step);
            }
        }

        _db.ApprovalChains.Add(chain);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created approval chain {ChainId} '{Name}' for organization {OrgId}",
            chain.Id, chain.Name, organizationId);

        return chain;
    }

    public async Task<ApprovalChain?> GetChainByIdAsync(
        Guid chainId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        return await _db.ApprovalChains
            .Include(c => c.Steps.OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync(c =>
                c.Id == chainId &&
                c.OrganizationId == organizationId,
                ct);
    }

    public async Task<List<ApprovalChain>> GetChainsAsync(
        Guid organizationId,
        bool? isActive = null,
        CancellationToken ct = default)
    {
        var query = _db.ApprovalChains
            .Include(c => c.Steps.OrderBy(s => s.StepOrder))
            .Where(c => c.OrganizationId == organizationId);

        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
    }

    public async Task<ApprovalChain> UpdateChainAsync(
        Guid chainId,
        UpdateApprovalChainRequest request,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var chain = await _db.ApprovalChains
            .Include(c => c.Steps)
            .FirstOrDefaultAsync(c =>
                c.Id == chainId &&
                c.OrganizationId == organizationId,
                ct)
            ?? throw new InvalidOperationException("Approval chain not found");

        if (request.Name != null) chain.Name = request.Name;
        if (request.Description != null) chain.Description = request.Description;
        if (request.TriggerEvent != null) chain.TriggerEvent = request.TriggerEvent;
        if (request.TriggerConditions != null) chain.TriggerConditions = request.TriggerConditions;
        if (request.IsActive.HasValue) chain.IsActive = request.IsActive.Value;
        if (request.IsParallel.HasValue) chain.IsParallel = request.IsParallel.Value;
        if (request.MinApprovalsRequired.HasValue) chain.MinApprovalsRequired = request.MinApprovalsRequired;
        if (request.DefaultTimeoutHours.HasValue) chain.DefaultTimeoutHours = request.DefaultTimeoutHours;

        chain.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return chain;
    }

    public async Task DeleteChainAsync(
        Guid chainId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var chain = await _db.ApprovalChains
            .FirstOrDefaultAsync(c =>
                c.Id == chainId &&
                c.OrganizationId == organizationId,
                ct)
            ?? throw new InvalidOperationException("Approval chain not found");

        // Check for pending requests
        var hasPendingRequests = await _db.ApprovalRequests
            .AnyAsync(r =>
                r.ApprovalChainId == chainId &&
                r.Status == ApprovalStatus.Pending,
                ct);

        if (hasPendingRequests)
        {
            throw new InvalidOperationException("Cannot delete chain with pending approval requests");
        }

        chain.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted approval chain {ChainId}", chainId);
    }

    #endregion

    #region Approval Request Processing

    public async Task<ApprovalRequest> SubmitForApprovalAsync(
        SubmitApprovalRequest request,
        Guid organizationId,
        Guid userId,
        CancellationToken ct = default)
    {
        // Validate chain exists and is active
        var chain = await _db.ApprovalChains
            .Include(c => c.Steps.OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync(c =>
                c.Id == request.ApprovalChainId &&
                c.OrganizationId == organizationId &&
                c.IsActive,
                ct)
            ?? throw new InvalidOperationException("Approval chain not found or inactive");

        if (!chain.Steps.Any())
        {
            throw new InvalidOperationException("Approval chain has no steps configured");
        }

        // Validate notice exists
        var notice = await _db.Notices
            .FirstOrDefaultAsync(n =>
                n.Id == request.NoticeId &&
                n.OrganizationId == organizationId,
                ct)
            ?? throw new InvalidOperationException("Notice not found");

        // Check for existing pending request
        var existingPending = await _db.ApprovalRequests
            .AnyAsync(r =>
                r.NoticeId == request.NoticeId &&
                r.ApprovalChainId == request.ApprovalChainId &&
                r.Status == ApprovalStatus.Pending,
                ct);

        if (existingPending)
        {
            throw new InvalidOperationException("An approval request is already pending for this notice");
        }

        var firstStep = chain.Steps.First();
        var timeoutHours = firstStep.TimeoutHours ?? chain.DefaultTimeoutHours;

        var approvalRequest = new ApprovalRequest
        {
            Id = Guid.NewGuid(),
            ApprovalChainId = chain.Id,
            NoticeId = request.NoticeId,
            ResponseId = request.ResponseId,
            RequestedById = userId,
            CurrentStep = 1,
            Status = ApprovalStatus.Pending,
            CurrentStepDeadline = timeoutHours.HasValue
                ? DateTime.UtcNow.AddHours(timeoutHours.Value)
                : null,
            RequestNotes = request.Notes,
            Metadata = request.Metadata,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ApprovalRequests.Add(approvalRequest);
        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "approval.submitted",
            EntityType = "ApprovalRequest",
            EntityId = approvalRequest.Id,
            UserId = userId,
            OrganizationId = organizationId,
            NewValues = new Dictionary<string, object>
            {
                ["notice_id"] = request.NoticeId,
                ["chain_id"] = chain.Id,
                ["chain_name"] = chain.Name
            }
        });

        _logger.LogInformation(
            "Approval request {RequestId} submitted for notice {NoticeId} using chain '{ChainName}'",
            approvalRequest.Id, request.NoticeId, chain.Name);

        return approvalRequest;
    }

    public async Task<ApprovalRequest?> GetRequestByIdAsync(
        Guid requestId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        return await _db.ApprovalRequests
            .Include(r => r.ApprovalChain)
                .ThenInclude(c => c.Steps.OrderBy(s => s.StepOrder))
            .Include(r => r.Actions.OrderBy(a => a.CreatedAt))
                .ThenInclude(a => a.Actor)
            .Include(r => r.RequestedBy)
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r =>
                r.Id == requestId &&
                r.ApprovalChain.OrganizationId == organizationId,
                ct);
    }

    public async Task<List<ApprovalRequest>> GetPendingApprovalsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken ct = default)
    {
        // Get pending requests where user is the current approver
        var requests = await _db.ApprovalRequests
            .Include(r => r.ApprovalChain)
                .ThenInclude(c => c.Steps)
            .Include(r => r.Notice)
            .Include(r => r.RequestedBy)
            .Where(r =>
                r.ApprovalChain.OrganizationId == organizationId &&
                r.Status == ApprovalStatus.Pending)
            .ToListAsync(ct);

        // Filter to requests where user is the approver for current step
        return requests.Where(r =>
        {
            var currentStep = r.ApprovalChain.Steps
                .FirstOrDefault(s => s.StepOrder == r.CurrentStep);

            if (currentStep == null) return false;

            return currentStep.ApproverType switch
            {
                ApproverType.User => currentStep.ApproverId == userId,
                // For role-based, would need to check user roles
                _ => currentStep.ApproverId == userId
            };
        }).ToList();
    }

    public async Task<List<ApprovalRequest>> GetRequestsByNoticeAsync(
        Guid noticeId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        return await _db.ApprovalRequests
            .Include(r => r.ApprovalChain)
            .Include(r => r.Actions.OrderBy(a => a.CreatedAt))
                .ThenInclude(a => a.Actor)
            .Include(r => r.RequestedBy)
            .Where(r =>
                r.NoticeId == noticeId &&
                r.ApprovalChain.OrganizationId == organizationId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    #endregion

    #region Approval Actions

    public async Task<ApprovalRequest> ApproveAsync(
        Guid requestId,
        string? comments,
        Guid userId,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var request = await GetRequestWithValidation(requestId, userId, ct);
        var currentStep = GetCurrentStep(request);

        // Record the approval action
        var action = new ApprovalAction
        {
            Id = Guid.NewGuid(),
            ApprovalRequestId = requestId,
            ApprovalStepId = currentStep.Id,
            ActorId = userId,
            ActionType = ApprovalActionType.Approved,
            Comments = comments,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ApprovalActions.Add(action);

        // Check if there are more steps
        var nextStep = request.ApprovalChain.Steps
            .Where(s => s.StepOrder > request.CurrentStep)
            .OrderBy(s => s.StepOrder)
            .FirstOrDefault();

        if (nextStep != null)
        {
            // Move to next step
            request.CurrentStep = nextStep.StepOrder;
            var timeoutHours = nextStep.TimeoutHours ?? request.ApprovalChain.DefaultTimeoutHours;
            request.CurrentStepDeadline = timeoutHours.HasValue
                ? DateTime.UtcNow.AddHours(timeoutHours.Value)
                : null;
        }
        else
        {
            // All steps completed - mark as approved
            request.Status = ApprovalStatus.Approved;
            request.CompletedAt = DateTime.UtcNow;
        }

        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "approval.approved",
            EntityType = "ApprovalRequest",
            EntityId = requestId,
            UserId = userId,
            NewValues = new Dictionary<string, object>
            {
                ["step"] = currentStep.StepOrder,
                ["step_name"] = currentStep.Name,
                ["final"] = request.Status == ApprovalStatus.Approved
            }
        });

        _logger.LogInformation(
            "Approval request {RequestId} step {Step} approved by user {UserId}",
            requestId, currentStep.StepOrder, userId);

        return request;
    }

    public async Task<ApprovalRequest> RejectAsync(
        Guid requestId,
        string comments,
        Guid userId,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comments))
        {
            throw new ArgumentException("Rejection reason is required");
        }

        var request = await GetRequestWithValidation(requestId, userId, ct);
        var currentStep = GetCurrentStep(request);

        var action = new ApprovalAction
        {
            Id = Guid.NewGuid(),
            ApprovalRequestId = requestId,
            ApprovalStepId = currentStep.Id,
            ActorId = userId,
            ActionType = ApprovalActionType.Rejected,
            Comments = comments,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ApprovalActions.Add(action);

        request.Status = ApprovalStatus.Rejected;
        request.CompletedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "approval.rejected",
            EntityType = "ApprovalRequest",
            EntityId = requestId,
            UserId = userId,
            NewValues = new Dictionary<string, object>
            {
                ["step"] = currentStep.StepOrder,
                ["reason"] = comments
            }
        });

        _logger.LogInformation(
            "Approval request {RequestId} rejected by user {UserId}",
            requestId, userId);

        return request;
    }

    public async Task<ApprovalRequest> DelegateAsync(
        Guid requestId,
        Guid delegateToUserId,
        string? reason,
        Guid userId,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var request = await GetRequestWithValidation(requestId, userId, ct);
        var currentStep = GetCurrentStep(request);

        if (!currentStep.AllowDelegation)
        {
            throw new InvalidOperationException("Delegation is not allowed for this step");
        }

        // Verify delegate user exists
        var delegateUser = await _db.Users.FindAsync([delegateToUserId], ct)
            ?? throw new InvalidOperationException("Delegate user not found");

        var action = new ApprovalAction
        {
            Id = Guid.NewGuid(),
            ApprovalRequestId = requestId,
            ApprovalStepId = currentStep.Id,
            ActorId = userId,
            ActionType = ApprovalActionType.Delegated,
            DelegatedToId = delegateToUserId,
            DelegationReason = reason,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ApprovalActions.Add(action);

        // Update the step to use the delegated user
        currentStep.ApproverId = delegateToUserId;
        currentStep.UpdatedAt = DateTime.UtcNow;

        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditService.LogAsync(new AuditLogEntry
        {
            Action = "approval.delegated",
            EntityType = "ApprovalRequest",
            EntityId = requestId,
            UserId = userId,
            NewValues = new Dictionary<string, object>
            {
                ["delegated_to"] = delegateToUserId,
                ["reason"] = reason ?? ""
            }
        });

        _logger.LogInformation(
            "Approval request {RequestId} delegated from {FromUser} to {ToUser}",
            requestId, userId, delegateToUserId);

        return request;
    }

    public async Task<ApprovalRequest> RecallAsync(
        Guid requestId,
        Guid userId,
        CancellationToken ct = default)
    {
        var request = await _db.ApprovalRequests
            .Include(r => r.ApprovalChain)
                .ThenInclude(c => c.Steps)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Approval request not found");

        if (request.Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Can only recall pending requests");
        }

        if (request.RequestedById != userId)
        {
            throw new InvalidOperationException("Only the requester can recall the request");
        }

        var currentStep = GetCurrentStep(request);

        var action = new ApprovalAction
        {
            Id = Guid.NewGuid(),
            ApprovalRequestId = requestId,
            ApprovalStepId = currentStep.Id,
            ActorId = userId,
            ActionType = ApprovalActionType.Recalled,
            Comments = "Request recalled by submitter",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.ApprovalActions.Add(action);

        request.Status = ApprovalStatus.Cancelled;
        request.CompletedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Approval request {RequestId} recalled by user {UserId}",
            requestId, userId);

        return request;
    }

    #endregion

    #region Escalation

    public async Task ProcessEscalationsAsync(CancellationToken ct = default)
    {
        var expiredRequests = await _db.ApprovalRequests
            .Include(r => r.ApprovalChain)
                .ThenInclude(c => c.Steps)
            .Where(r =>
                r.Status == ApprovalStatus.Pending &&
                r.CurrentStepDeadline.HasValue &&
                r.CurrentStepDeadline < DateTime.UtcNow)
            .ToListAsync(ct);

        foreach (var request in expiredRequests)
        {
            try
            {
                var currentStep = GetCurrentStep(request);

                if (currentStep.EscalationUserId.HasValue)
                {
                    // Escalate to configured user
                    var action = new ApprovalAction
                    {
                        Id = Guid.NewGuid(),
                        ApprovalRequestId = request.Id,
                        ApprovalStepId = currentStep.Id,
                        ActorId = currentStep.EscalationUserId.Value,
                        ActionType = ApprovalActionType.Escalated,
                        Comments = "Automatically escalated due to timeout",
                        IsAutomatic = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.ApprovalActions.Add(action);

                    // Update approver to escalation user
                    currentStep.ApproverId = currentStep.EscalationUserId;

                    // Reset deadline
                    var timeoutHours = currentStep.TimeoutHours ?? request.ApprovalChain.DefaultTimeoutHours;
                    request.CurrentStepDeadline = timeoutHours.HasValue
                        ? DateTime.UtcNow.AddHours(timeoutHours.Value)
                        : null;

                    _logger.LogInformation(
                        "Escalated approval request {RequestId} step {Step} to user {UserId}",
                        request.Id, currentStep.StepOrder, currentStep.EscalationUserId);
                }
                else
                {
                    // No escalation user - mark as expired
                    request.Status = ApprovalStatus.Expired;
                    request.CompletedAt = DateTime.UtcNow;

                    var action = new ApprovalAction
                    {
                        Id = Guid.NewGuid(),
                        ApprovalRequestId = request.Id,
                        ApprovalStepId = currentStep.Id,
                        ActorId = request.RequestedById,
                        ActionType = ApprovalActionType.Escalated,
                        Comments = "Request expired due to timeout with no escalation configured",
                        IsAutomatic = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.ApprovalActions.Add(action);

                    _logger.LogWarning(
                        "Approval request {RequestId} expired - no escalation user configured",
                        request.Id);
                }

                request.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process escalation for request {RequestId}",
                    request.Id);
            }
        }

        if (expiredRequests.Any())
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    #endregion

    #region Helpers

    private async Task<ApprovalRequest> GetRequestWithValidation(
        Guid requestId,
        Guid userId,
        CancellationToken ct)
    {
        var request = await _db.ApprovalRequests
            .Include(r => r.ApprovalChain)
                .ThenInclude(c => c.Steps.OrderBy(s => s.StepOrder))
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new InvalidOperationException("Approval request not found");

        if (request.Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException($"Request is not pending (status: {request.Status})");
        }

        var currentStep = GetCurrentStep(request);

        // Validate user is the approver
        if (currentStep.ApproverType == ApproverType.User && currentStep.ApproverId != userId)
        {
            throw new InvalidOperationException("You are not authorized to approve this request");
        }

        return request;
    }

    private static ApprovalStep GetCurrentStep(ApprovalRequest request)
    {
        return request.ApprovalChain.Steps
            .FirstOrDefault(s => s.StepOrder == request.CurrentStep)
            ?? throw new InvalidOperationException("Current approval step not found");
    }

    #endregion
}
