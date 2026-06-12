using EffortlessInsight.Api.DTOs;
using Microsoft.AspNetCore.Http;

namespace EffortlessInsight.Api.Services.Collaboration;

public interface IDocumentRequestService
{
    // Document Request CRUD
    Task<DocumentRequestDto> CreateDocumentRequestAsync(Guid noticeId, CreateDocumentRequestDto dto, Guid userId);
    Task<DocumentRequestDto> GetDocumentRequestByIdAsync(Guid requestId, Guid userId);
    Task<DocumentRequestListResponseDto> GetDocumentRequestsForNoticeAsync(Guid noticeId, Guid userId, string? status = null);
    Task<DocumentRequestDto> UpdateDocumentRequestAsync(Guid requestId, UpdateDocumentRequestDto dto, Guid userId);
    Task DeleteDocumentRequestAsync(Guid requestId, Guid userId);

    // Fulfillment
    Task<DocumentRequestDto> FulfillDocumentRequestAsync(Guid requestId, IFormFile file, string? note, Guid userId);
    Task<DocumentRequestDto> MarkAsFulfilledAsync(Guid requestId, Guid userId, string? note = null);
    Task<DocumentRequestDto> ReviewDocumentRequestAsync(Guid requestId, string status, string? reviewNote, Guid userId);

    // Templates
    Task<DocumentRequestTemplateDto> CreateDocumentRequestTemplateAsync(CreateDocumentRequestTemplateDto dto, Guid organizationId);
    Task<List<DocumentRequestTemplateDto>> GetDocumentRequestTemplatesAsync(Guid organizationId, string? noticeType = null);
    Task DeleteDocumentRequestTemplateAsync(Guid templateId, Guid organizationId);

    // Reminders
    Task SendReminderAsync(Guid requestId, Guid userId);

    // User's document requests
    Task<List<DocumentRequestDto>> GetMyPendingRequestsAsync(Guid userId);
}
