using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.DTOs;
using EffortlessInsight.Api.Services.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.Collaboration;

public class DocumentRequestService : IDocumentRequestService
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityService _activityService;
    private readonly INotificationService _notificationService;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<DocumentRequestService> _logger;

    private static readonly string[] AllowedMimeTypes =
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    public DocumentRequestService(
        ApplicationDbContext context,
        IActivityService activityService,
        INotificationService notificationService,
        IFileStorageService fileStorage,
        ILogger<DocumentRequestService> logger)
    {
        _context = context;
        _activityService = activityService;
        _notificationService = notificationService;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<DocumentRequestDto> CreateDocumentRequestAsync(Guid noticeId, CreateDocumentRequestDto dto, Guid userId)
    {
        var notice = await _context.Notices
            .FirstOrDefaultAsync(n => n.Id == noticeId)
            ?? throw new KeyNotFoundException("Notice not found");

        // Verify notice is not closed
        if (notice.Status == NoticeStatus.Closed || notice.Status == NoticeStatus.Archived)
        {
            throw new InvalidOperationException("Cannot create document request for closed notice");
        }

        // Verify requested user exists
        var requestedFrom = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == dto.RequestedFrom)
            ?? throw new KeyNotFoundException("Requested user not found");

        // Apply template if specified
        DocumentRequestTemplate? template = null;
        if (dto.TemplateId.HasValue)
        {
            template = await _context.DocumentRequestTemplates
                .FirstOrDefaultAsync(t => t.Id == dto.TemplateId.Value && t.IsActive);
        }

        var request = new DocumentRequest
        {
            NoticeId = noticeId,
            Title = dto.Title,
            Description = dto.Description,
            Status = DocumentRequestStatus.Pending,
            Priority = dto.Priority ?? template?.DefaultPriority ?? TaskPriorityValues.Medium,
            DueDate = dto.DueDate,
            AcceptedFormats = dto.AcceptedFormats ?? template?.AcceptedFormats,
            RequestedFromId = dto.RequestedFrom,
            RequestedById = userId,
            TemplateId = dto.TemplateId
        };

        _context.DocumentRequests.Add(request);
        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            notice.OrganizationId,
            noticeId,
            ActivityTypes.DocumentRequested,
            userId,
            new Dictionary<string, object>
            {
                ["requestId"] = request.Id,
                ["title"] = request.Title,
                ["requestedFromId"] = dto.RequestedFrom,
                ["requestedFromName"] = requestedFrom.Name,
                ["dueDate"] = dto.DueDate.ToString("yyyy-MM-dd")
            },
            $"requested document \"{request.Title}\" from {requestedFrom.Name}"
        );

        // Send notification to the requested user (fire and forget)
        _ = _notificationService.NotifyDocumentRequestedAsync(request);

        return await GetDocumentRequestByIdAsync(request.Id, userId);
    }

    public async Task<DocumentRequestDto> GetDocumentRequestByIdAsync(Guid requestId, Guid userId)
    {
        var request = await _context.DocumentRequests
            .Include(r => r.RequestedFrom)
            .Include(r => r.RequestedBy)
            .Include(r => r.ReviewedBy)
            .Include(r => r.Documents).ThenInclude(d => d.File)
            .Include(r => r.Documents).ThenInclude(d => d.UploadedBy)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        return MapToDto(request);
    }

    public async Task<DocumentRequestListResponseDto> GetDocumentRequestsForNoticeAsync(
        Guid noticeId,
        Guid userId,
        string? status = null)
    {
        var query = _context.DocumentRequests
            .Include(r => r.RequestedFrom)
            .Include(r => r.RequestedBy)
            .Include(r => r.ReviewedBy)
            .Include(r => r.Documents).ThenInclude(d => d.File)
            .Include(r => r.Documents).ThenInclude(d => d.UploadedBy)
            .Where(r => r.NoticeId == noticeId);

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var requests = await query
            .OrderByDescending(r => r.Priority == TaskPriorityValues.Critical)
            .ThenByDescending(r => r.Priority == TaskPriorityValues.High)
            .ThenBy(r => r.DueDate)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync();

        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = new DocumentRequestSummaryDto(
            Total: requests.Count,
            Pending: requests.Count(r => r.Status == DocumentRequestStatus.Pending),
            Submitted: requests.Count(r => r.Status == DocumentRequestStatus.Submitted),
            Reviewing: requests.Count(r => r.Status == DocumentRequestStatus.Reviewing),
            Fulfilled: requests.Count(r => r.Status == DocumentRequestStatus.Fulfilled),
            ResubmitNeeded: requests.Count(r => r.Status == DocumentRequestStatus.ResubmitNeeded),
            Overdue: requests.Count(r => IsOverdue(r))
        );

        return new DocumentRequestListResponseDto(
            Requests: requests.Select(MapToDto).ToList(),
            Summary: summary
        );
    }

    public async Task<DocumentRequestDto> UpdateDocumentRequestAsync(Guid requestId, UpdateDocumentRequestDto dto, Guid userId)
    {
        var request = await _context.DocumentRequests
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        if (dto.Title != null) request.Title = dto.Title;
        if (dto.Description != null) request.Description = dto.Description;
        if (dto.DueDate.HasValue) request.DueDate = dto.DueDate.Value;
        if (dto.Priority != null) request.Priority = dto.Priority;
        if (dto.AcceptedFormats != null) request.AcceptedFormats = dto.AcceptedFormats;
        if (dto.Status != null)
        {
            ValidateStatusTransition(request.Status, dto.Status);
            request.Status = dto.Status;
        }

        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return await GetDocumentRequestByIdAsync(request.Id, userId);
    }

    public async Task DeleteDocumentRequestAsync(Guid requestId, Guid userId)
    {
        var request = await _context.DocumentRequests
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        request.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<DocumentRequestDto> FulfillDocumentRequestAsync(
        Guid requestId,
        IFormFile file,
        string? note,
        Guid userId)
    {
        var request = await _context.DocumentRequests
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        // Validate file
        await ValidateFileAsync(file, request.AcceptedFormats);

        // Upload file
        var storagePath = $"documents/{request.Notice.OrganizationId}/{request.NoticeId}/{Guid.NewGuid()}/{file.FileName}";
        var uploadResult = await _fileStorage.UploadAsync(file.OpenReadStream(), storagePath, file.ContentType);

        // Create file record
        var noticeFile = new NoticeFile
        {
            OrganizationId = request.Notice.OrganizationId,
            NoticeId = request.NoticeId,
            Filename = file.FileName,
            OriginalFilename = file.FileName,
            MimeType = file.ContentType,
            SizeBytes = file.Length,
            StoragePath = storagePath,
            StorageProvider = "s3",
            UploadedById = userId
        };

        _context.NoticeFiles.Add(noticeFile);

        // Create document request document
        var docRequestDoc = new DocumentRequestDocument
        {
            RequestId = requestId,
            FileId = noticeFile.Id,
            Note = note,
            UploadedById = userId,
            UploadedAt = DateTime.UtcNow
        };

        _context.DocumentRequestDocuments.Add(docRequestDoc);

        // Update request status
        request.Status = DocumentRequestStatus.Submitted;
        request.FulfilledAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            request.Notice.OrganizationId,
            request.NoticeId,
            ActivityTypes.DocumentUploaded,
            userId,
            new Dictionary<string, object>
            {
                ["requestId"] = requestId,
                ["requestTitle"] = request.Title,
                ["documentId"] = noticeFile.Id,
                ["filename"] = file.FileName,
                ["size"] = file.Length
            },
            $"uploaded document for \"{request.Title}\""
        );

        // Send notification to the requester (fire and forget)
        _ = _notificationService.NotifyDocumentSubmittedAsync(request);

        return await GetDocumentRequestByIdAsync(requestId, userId);
    }

    public async Task<DocumentRequestDto> MarkAsFulfilledAsync(Guid requestId, Guid userId, string? note = null)
    {
        var request = await _context.DocumentRequests
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        request.Status = DocumentRequestStatus.Fulfilled;
        request.FulfilledAt = DateTime.UtcNow;
        request.ReviewedById = userId;
        request.ReviewNote = note ?? "Manually marked as fulfilled";
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await GetDocumentRequestByIdAsync(requestId, userId);
    }

    public async Task<DocumentRequestDto> ReviewDocumentRequestAsync(
        Guid requestId,
        string status,
        string? reviewNote,
        Guid userId)
    {
        var request = await _context.DocumentRequests
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        if (request.Status != DocumentRequestStatus.Submitted)
        {
            throw new InvalidOperationException("Can only review submitted documents");
        }

        if (status != DocumentRequestStatus.Fulfilled && status != DocumentRequestStatus.ResubmitNeeded)
        {
            throw new InvalidOperationException("Invalid review status");
        }

        request.Status = status;
        request.ReviewedById = userId;
        request.ReviewNote = reviewNote;
        request.UpdatedAt = DateTime.UtcNow;

        if (status == DocumentRequestStatus.Fulfilled)
        {
            request.FulfilledAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        // Log activity
        await _activityService.LogActivityAsync(
            request.Notice.OrganizationId,
            request.NoticeId,
            ActivityTypes.DocumentReviewed,
            userId,
            new Dictionary<string, object>
            {
                ["requestId"] = requestId,
                ["requestTitle"] = request.Title,
                ["status"] = status,
                ["reviewNote"] = reviewNote ?? ""
            },
            status == DocumentRequestStatus.Fulfilled
                ? $"approved document \"{request.Title}\""
                : $"requested resubmission for \"{request.Title}\""
        );

        // Send notification to the submitter (fire and forget)
        _ = _notificationService.NotifyDocumentReviewedAsync(request, status == DocumentRequestStatus.Fulfilled);

        return await GetDocumentRequestByIdAsync(requestId, userId);
    }

    public async Task<DocumentRequestTemplateDto> CreateDocumentRequestTemplateAsync(
        CreateDocumentRequestTemplateDto dto,
        Guid organizationId)
    {
        var template = new DocumentRequestTemplate
        {
            OrganizationId = organizationId,
            Name = dto.Name,
            TitleTemplate = dto.TitleTemplate,
            DescriptionTemplate = dto.DescriptionTemplate,
            DefaultPriority = dto.DefaultPriority ?? TaskPriorityValues.Medium,
            DefaultDueDays = dto.DefaultDueDays ?? 7,
            AcceptedFormats = dto.AcceptedFormats,
            ApplicableNoticeTypes = dto.ApplicableNoticeTypes ?? new List<string> { "*" },
            IsActive = true
        };

        _context.DocumentRequestTemplates.Add(template);
        await _context.SaveChangesAsync();

        return MapToTemplateDto(template);
    }

    public async Task<List<DocumentRequestTemplateDto>> GetDocumentRequestTemplatesAsync(
        Guid organizationId,
        string? noticeType = null)
    {
        var query = _context.DocumentRequestTemplates
            .Where(t => t.IsActive && (t.OrganizationId == null || t.OrganizationId == organizationId));

        if (!string.IsNullOrEmpty(noticeType))
        {
            query = query.Where(t =>
                t.ApplicableNoticeTypes == null ||
                t.ApplicableNoticeTypes.Contains("*") ||
                t.ApplicableNoticeTypes.Contains(noticeType));
        }

        var templates = await query.OrderBy(t => t.Name).ToListAsync();

        return templates.Select(MapToTemplateDto).ToList();
    }

    public async Task DeleteDocumentRequestTemplateAsync(Guid templateId, Guid organizationId)
    {
        var template = await _context.DocumentRequestTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == organizationId)
            ?? throw new KeyNotFoundException("Template not found");

        template.DeletedAt = DateTime.UtcNow;
        template.IsActive = false;
        await _context.SaveChangesAsync();
    }

    public async Task SendReminderAsync(Guid requestId, Guid userId)
    {
        var request = await _context.DocumentRequests
            .Include(r => r.RequestedFrom)
            .Include(r => r.RequestedBy)
            .Include(r => r.Notice)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException("Document request not found");

        // Send reminder notification
        await _notificationService.SendDocumentRequestReminderAsync(request);

        _logger.LogInformation(
            "Reminder sent for document request {RequestId} to user {UserId}",
            requestId,
            request.RequestedFromId);
    }

    public async Task<List<DocumentRequestDto>> GetMyPendingRequestsAsync(Guid userId)
    {
        var requests = await _context.DocumentRequests
            .Include(r => r.RequestedFrom)
            .Include(r => r.RequestedBy)
            .Include(r => r.Notice)
            .Where(r => r.RequestedFromId == userId &&
                       (r.Status == DocumentRequestStatus.Pending ||
                        r.Status == DocumentRequestStatus.ResubmitNeeded))
            .OrderBy(r => r.DueDate)
            .ToListAsync();

        return requests.Select(MapToDto).ToList();
    }

    // Private helpers

    private async Task ValidateFileAsync(IFormFile file, List<string>? acceptedFormats)
    {
        if (file.Length > MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File exceeds maximum size of {MaxFileSizeBytes / 1024 / 1024}MB");
        }

        if (!AllowedMimeTypes.Contains(file.ContentType))
        {
            throw new InvalidOperationException($"File type {file.ContentType} not allowed");
        }

        var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();

        if (acceptedFormats?.Any() == true)
        {
            if (!acceptedFormats.Any(f => f.Equals(extension, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Only {string.Join(", ", acceptedFormats)} files are accepted");
            }
        }

        // Magic byte verification to prevent MIME type spoofing
        await VerifyFileMagicBytesAsync(file, extension);
    }

    private static async Task VerifyFileMagicBytesAsync(IFormFile file, string extension)
    {
        // Read first 8 bytes for magic byte verification
        var headerBytes = new byte[8];
        using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(headerBytes.AsMemory(0, 8));

        if (bytesRead < 4)
        {
            throw new InvalidOperationException("File is too small to verify");
        }

        // Verify magic bytes match the claimed file extension
        var isValid = extension switch
        {
            // PDF: %PDF (25 50 44 46)
            "pdf" => headerBytes[0] == 0x25 && headerBytes[1] == 0x50 &&
                     headerBytes[2] == 0x44 && headerBytes[3] == 0x46,

            // ZIP/DOCX/XLSX/PPTX: PK (50 4B 03 04)
            "zip" or "docx" or "xlsx" or "pptx" =>
                headerBytes[0] == 0x50 && headerBytes[1] == 0x4B &&
                headerBytes[2] == 0x03 && headerBytes[3] == 0x04,

            // DOC/XLS/PPT (OLE Compound): D0 CF 11 E0
            "doc" or "xls" or "ppt" =>
                headerBytes[0] == 0xD0 && headerBytes[1] == 0xCF &&
                headerBytes[2] == 0x11 && headerBytes[3] == 0xE0,

            // JPEG: FF D8 FF
            "jpg" or "jpeg" =>
                headerBytes[0] == 0xFF && headerBytes[1] == 0xD8 && headerBytes[2] == 0xFF,

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            "png" => headerBytes[0] == 0x89 && headerBytes[1] == 0x50 &&
                     headerBytes[2] == 0x4E && headerBytes[3] == 0x47 &&
                     headerBytes[4] == 0x0D && headerBytes[5] == 0x0A &&
                     headerBytes[6] == 0x1A && headerBytes[7] == 0x0A,

            // GIF: GIF87a or GIF89a (47 49 46 38)
            "gif" => headerBytes[0] == 0x47 && headerBytes[1] == 0x49 &&
                     headerBytes[2] == 0x46 && headerBytes[3] == 0x38,

            // For unknown extensions, skip magic byte check but log warning
            _ => true
        };

        if (!isValid)
        {
            throw new InvalidOperationException(
                $"File content does not match the expected format for .{extension} files. " +
                "This may indicate a mislabeled or potentially malicious file.");
        }
    }

    private static void ValidateStatusTransition(string current, string next)
    {
        var validTransitions = new Dictionary<string, string[]>
        {
            [DocumentRequestStatus.Pending] = new[] { DocumentRequestStatus.Submitted, DocumentRequestStatus.Cancelled },
            [DocumentRequestStatus.Submitted] = new[] { DocumentRequestStatus.Reviewing, DocumentRequestStatus.Fulfilled, DocumentRequestStatus.ResubmitNeeded },
            [DocumentRequestStatus.Reviewing] = new[] { DocumentRequestStatus.Fulfilled, DocumentRequestStatus.ResubmitNeeded },
            [DocumentRequestStatus.ResubmitNeeded] = new[] { DocumentRequestStatus.Submitted, DocumentRequestStatus.Cancelled },
            [DocumentRequestStatus.Fulfilled] = Array.Empty<string>(),
            [DocumentRequestStatus.Cancelled] = Array.Empty<string>()
        };

        if (!validTransitions.TryGetValue(current, out var allowed) || !allowed.Contains(next))
        {
            throw new InvalidOperationException($"Invalid status transition from {current} to {next}");
        }
    }

    private static bool IsOverdue(DocumentRequest request)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        return request.DueDate < now &&
               request.Status != DocumentRequestStatus.Fulfilled &&
               request.Status != DocumentRequestStatus.Cancelled;
    }

    private static int GetDaysRemaining(DateOnly dueDate)
    {
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        return dueDate.DayNumber - now.DayNumber;
    }

    private static DocumentRequestDto MapToDto(DocumentRequest request)
    {
        return new DocumentRequestDto(
            Id: request.Id,
            NoticeId: request.NoticeId,
            Title: request.Title,
            Description: request.Description,
            Status: request.Status,
            Priority: request.Priority,
            DueDate: request.DueDate,
            IsOverdue: IsOverdue(request),
            DaysRemaining: GetDaysRemaining(request.DueDate),
            AcceptedFormats: request.AcceptedFormats,
            RequestedFrom: new DocumentRequestUserDto(
                Id: request.RequestedFromId,
                Name: request.RequestedFrom?.Name ?? "Unknown",
                Email: request.RequestedFrom?.Email,
                AvatarUrl: request.RequestedFrom?.AvatarUrl
            ),
            RequestedBy: new DocumentRequestUserDto(
                Id: request.RequestedById,
                Name: request.RequestedBy?.Name ?? "Unknown",
                Email: request.RequestedBy?.Email,
                AvatarUrl: request.RequestedBy?.AvatarUrl
            ),
            FulfilledAt: request.FulfilledAt,
            ReviewedBy: request.ReviewedBy != null ? new DocumentRequestUserDto(
                Id: request.ReviewedBy.Id,
                Name: request.ReviewedBy.Name,
                Email: request.ReviewedBy.Email,
                AvatarUrl: request.ReviewedBy.AvatarUrl
            ) : null,
            ReviewNote: request.ReviewNote,
            Documents: request.Documents?.Select(d => new DocumentRequestDocumentDto(
                Id: d.Id,
                FileId: d.FileId,
                Filename: d.File?.OriginalFilename ?? "Unknown",
                SizeBytes: d.File?.SizeBytes ?? 0,
                MimeType: d.File?.MimeType ?? "application/octet-stream",
                UploadedBy: new DocumentRequestUserDto(
                    Id: d.UploadedById,
                    Name: d.UploadedBy?.Name ?? "Unknown",
                    Email: d.UploadedBy?.Email,
                    AvatarUrl: d.UploadedBy?.AvatarUrl
                ),
                UploadedAt: d.UploadedAt,
                Note: d.Note
            )).ToList() ?? new List<DocumentRequestDocumentDto>(),
            CreatedAt: request.CreatedAt,
            UpdatedAt: request.UpdatedAt
        );
    }

    private static DocumentRequestTemplateDto MapToTemplateDto(DocumentRequestTemplate template)
    {
        return new DocumentRequestTemplateDto(
            Id: template.Id,
            OrganizationId: template.OrganizationId,
            Name: template.Name,
            TitleTemplate: template.TitleTemplate,
            DescriptionTemplate: template.DescriptionTemplate,
            DefaultPriority: template.DefaultPriority,
            DefaultDueDays: template.DefaultDueDays,
            AcceptedFormats: template.AcceptedFormats,
            ApplicableNoticeTypes: template.ApplicableNoticeTypes,
            IsActive: template.IsActive,
            CreatedAt: template.CreatedAt
        );
    }
}
