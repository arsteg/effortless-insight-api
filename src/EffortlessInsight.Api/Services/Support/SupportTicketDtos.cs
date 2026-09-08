namespace EffortlessInsight.Api.Services.Support;

// ============================================================================
// In-app support ticket DTOs
// ============================================================================

public record SupportTicketSummaryDto(
    Guid Id,
    string Subject,
    string Category,
    string Status,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    int MessageCount,
    bool HasUnreadSupportReply);

public record SupportTicketMessageDto(
    Guid Id,
    bool IsFromSupport,
    string SenderName,
    string Body,
    DateTime CreatedAt);

public record SupportTicketDetailDto(
    Guid Id,
    string Subject,
    string Category,
    string Status,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    IReadOnlyList<SupportTicketMessageDto> Messages);

public record CreateSupportTicketRequest(
    string Subject,
    string Category,
    string Message);

public record SupportTicketReplyRequest(string Message);

public record SupportTicketStatusRequest(string Status);

// Admin-side list item includes organization context
public record AdminSupportTicketSummaryDto(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string CreatedByName,
    string CreatedByEmail,
    string Subject,
    string Category,
    string Status,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    int MessageCount);
