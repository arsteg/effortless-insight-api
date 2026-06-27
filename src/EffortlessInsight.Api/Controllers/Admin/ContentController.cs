using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.Admin;
using EffortlessInsight.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Controllers.Admin;

/// <summary>
/// Admin API endpoints for content management (FAQs, help articles, templates)
/// </summary>
[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/v1/admin/content")]
public class ContentController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ContentController> _logger;

    public ContentController(
        ApplicationDbContext context,
        ILogger<ContentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all content pages with optional filtering
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ContentListResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? contentType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.ContentPages
            .Where(c => c.DeletedAt == null)
            .AsQueryable();

        if (!string.IsNullOrEmpty(contentType))
            query = query.Where(c => c.ContentType == contentType);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(c => c.Status == status);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(c => c.Category == category);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(c =>
                c.Title.ToLower().Contains(search.ToLower()) ||
                c.Content.ToLower().Contains(search.ToLower()));

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(c => c.ContentType)
            .ThenBy(c => c.DisplayOrder)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ContentPageDto
            {
                Id = c.Id,
                ContentType = c.ContentType,
                Slug = c.Slug,
                Title = c.Title,
                Excerpt = c.Excerpt,
                Category = c.Category,
                Status = c.Status,
                Version = c.Version,
                DisplayOrder = c.DisplayOrder,
                IsFeatured = c.IsFeatured,
                ViewCount = c.ViewCount,
                HelpfulCount = c.HelpfulCount,
                NotHelpfulCount = c.NotHelpfulCount,
                PublishedAt = c.PublishedAt,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync();

        return Ok(new ApiResponse<ContentListResponse>(true, new ContentListResponse
        {
            Pages = items,
            Pagination = new PaginationInfo
            {
                Page = page,
                PageSize = pageSize,
                Total = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            }
        }));
    }

    /// <summary>
    /// Get content by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ContentPageDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var content = await _context.ContentPages
            .Where(c => c.Id == id && c.DeletedAt == null)
            .Select(c => new ContentPageDetailDto
            {
                Id = c.Id,
                ContentType = c.ContentType,
                Slug = c.Slug,
                Title = c.Title,
                Excerpt = c.Excerpt,
                Content = c.Content,
                ContentFormat = c.ContentFormat,
                Category = c.Category,
                Tags = c.Tags,
                Status = c.Status,
                Version = c.Version,
                DisplayOrder = c.DisplayOrder,
                IsFeatured = c.IsFeatured,
                AllowFeedback = c.AllowFeedback,
                ViewCount = c.ViewCount,
                HelpfulCount = c.HelpfulCount,
                NotHelpfulCount = c.NotHelpfulCount,
                MetaTitle = c.MetaTitle,
                MetaDescription = c.MetaDescription,
                Language = c.Language,
                PublishedAt = c.PublishedAt,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .FirstOrDefaultAsync();

        if (content == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Content not found"));

        return Ok(new ApiResponse<ContentPageDetailDto>(true, content));
    }

    /// <summary>
    /// Create new content page
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ContentPageDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateContentRequest request)
    {
        var adminId = GetAdminUserId();

        // Check slug uniqueness
        var slugExists = await _context.ContentPages
            .AnyAsync(c => c.Slug == request.Slug && c.DeletedAt == null);

        if (slugExists)
            return BadRequest(new ApiErrorResponse(false, "SLUG_EXISTS", "A content page with this slug already exists"));

        var content = new ContentPage
        {
            ContentType = request.ContentType,
            Slug = request.Slug,
            Title = request.Title,
            Excerpt = request.Excerpt,
            Content = request.Content,
            ContentFormat = request.ContentFormat ?? "markdown",
            Category = request.Category,
            Tags = request.Tags ?? [],
            Status = ContentStatus.Draft,
            DisplayOrder = request.DisplayOrder ?? 0,
            IsFeatured = request.IsFeatured ?? false,
            AllowFeedback = request.AllowFeedback ?? true,
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription,
            Language = request.Language ?? "en",
            CreatedById = adminId
        };

        _context.ContentPages.Add(content);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Content page created: {ContentId} by admin {AdminId}", content.Id, adminId);

        return CreatedAtAction(nameof(GetById), new { id = content.Id },
            new ApiResponse<ContentPageDetailDto>(true, MapToDetailDto(content)));
    }

    /// <summary>
    /// Update content page
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ContentPageDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateContentRequest request)
    {
        var adminId = GetAdminUserId();

        var content = await _context.ContentPages
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);

        if (content == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Content not found"));

        // Check slug uniqueness if changed
        if (!string.IsNullOrEmpty(request.Slug) && request.Slug != content.Slug)
        {
            var slugExists = await _context.ContentPages
                .AnyAsync(c => c.Slug == request.Slug && c.Id != id && c.DeletedAt == null);

            if (slugExists)
                return BadRequest(new ApiErrorResponse(false, "SLUG_EXISTS", "A content page with this slug already exists"));

            content.Slug = request.Slug;
        }

        if (request.Title != null) content.Title = request.Title;
        if (request.Excerpt != null) content.Excerpt = request.Excerpt;
        if (request.Content != null) content.Content = request.Content;
        if (request.ContentFormat != null) content.ContentFormat = request.ContentFormat;
        if (request.Category != null) content.Category = request.Category;
        if (request.Tags != null) content.Tags = request.Tags;
        if (request.DisplayOrder.HasValue) content.DisplayOrder = request.DisplayOrder.Value;
        if (request.IsFeatured.HasValue) content.IsFeatured = request.IsFeatured.Value;
        if (request.AllowFeedback.HasValue) content.AllowFeedback = request.AllowFeedback.Value;
        if (request.MetaTitle != null) content.MetaTitle = request.MetaTitle;
        if (request.MetaDescription != null) content.MetaDescription = request.MetaDescription;

        content.UpdatedAt = DateTime.UtcNow;
        content.UpdatedById = adminId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Content page updated: {ContentId} by admin {AdminId}", content.Id, adminId);

        return Ok(new ApiResponse<ContentPageDetailDto>(true, MapToDetailDto(content)));
    }

    /// <summary>
    /// Publish content page
    /// </summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(ApiResponse<ContentPageDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Publish(Guid id)
    {
        var adminId = GetAdminUserId();

        var content = await _context.ContentPages
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);

        if (content == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Content not found"));

        // Create version snapshot
        var version = new ContentPageVersion
        {
            ContentPageId = content.Id,
            Version = content.Version,
            Title = content.Title,
            Content = content.Content,
            CreatedById = adminId,
            ChangeNotes = "Published"
        };
        _context.ContentPageVersions.Add(version);

        // Update content
        content.Status = ContentStatus.Published;
        content.Version++;
        content.PublishedAt = DateTime.UtcNow;
        content.PublishedById = adminId;
        content.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Content page published: {ContentId} by admin {AdminId}", content.Id, adminId);

        return Ok(new ApiResponse<ContentPageDetailDto>(true, MapToDetailDto(content)));
    }

    /// <summary>
    /// Archive content page
    /// </summary>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(ApiResponse<ContentPageDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id)
    {
        var adminId = GetAdminUserId();

        var content = await _context.ContentPages
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);

        if (content == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Content not found"));

        content.Status = ContentStatus.Archived;
        content.UpdatedAt = DateTime.UtcNow;
        content.UpdatedById = adminId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Content page archived: {ContentId} by admin {AdminId}", content.Id, adminId);

        return Ok(new ApiResponse<ContentPageDetailDto>(true, MapToDetailDto(content)));
    }

    /// <summary>
    /// Delete content page
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var adminId = GetAdminUserId();

        var content = await _context.ContentPages
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);

        if (content == null)
            return NotFound(new ApiErrorResponse(false, "NOT_FOUND", "Content not found"));

        content.DeletedAt = DateTime.UtcNow;
        content.UpdatedById = adminId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Content page deleted: {ContentId} by admin {AdminId}", content.Id, adminId);

        return NoContent();
    }

    /// <summary>
    /// Get distinct categories
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories([FromQuery] string? contentType = null)
    {
        var query = _context.ContentPages
            .Where(c => c.DeletedAt == null && c.Category != null);

        if (!string.IsNullOrEmpty(contentType))
            query = query.Where(c => c.ContentType == contentType);

        var categories = await query
            .Select(c => c.Category!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return Ok(new ApiResponse<List<string>>(true, categories));
    }

    private Guid GetAdminUserId()
    {
        var claim = User.FindFirst("admin_id") ?? User.FindFirst("sub");
        return Guid.Parse(claim?.Value ?? throw new UnauthorizedAccessException("Admin user ID not found"));
    }

    private static ContentPageDetailDto MapToDetailDto(ContentPage c) => new()
    {
        Id = c.Id,
        ContentType = c.ContentType,
        Slug = c.Slug,
        Title = c.Title,
        Excerpt = c.Excerpt,
        Content = c.Content,
        ContentFormat = c.ContentFormat,
        Category = c.Category,
        Tags = c.Tags,
        Status = c.Status,
        Version = c.Version,
        DisplayOrder = c.DisplayOrder,
        IsFeatured = c.IsFeatured,
        AllowFeedback = c.AllowFeedback,
        ViewCount = c.ViewCount,
        HelpfulCount = c.HelpfulCount,
        NotHelpfulCount = c.NotHelpfulCount,
        MetaTitle = c.MetaTitle,
        MetaDescription = c.MetaDescription,
        Language = c.Language,
        PublishedAt = c.PublishedAt,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };
}

// DTOs
public record ContentListResponse
{
    public List<ContentPageDto> Pages { get; init; } = [];
    public PaginationInfo Pagination { get; init; } = new();
}

public record ContentPageDto
{
    public Guid Id { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Excerpt { get; init; }
    public string? Category { get; init; }
    public string Status { get; init; } = string.Empty;
    public int Version { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsFeatured { get; init; }
    public int ViewCount { get; init; }
    public int HelpfulCount { get; init; }
    public int NotHelpfulCount { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public record ContentPageDetailDto : ContentPageDto
{
    public string Content { get; init; } = string.Empty;
    public string ContentFormat { get; init; } = "markdown";
    public List<string> Tags { get; init; } = [];
    public bool AllowFeedback { get; init; }
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
    public string Language { get; init; } = "en";
}

public record CreateContentRequest
{
    public required string ContentType { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? Excerpt { get; init; }
    public required string Content { get; init; }
    public string? ContentFormat { get; init; }
    public string? Category { get; init; }
    public List<string>? Tags { get; init; }
    public int? DisplayOrder { get; init; }
    public bool? IsFeatured { get; init; }
    public bool? AllowFeedback { get; init; }
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
    public string? Language { get; init; }
}

public record UpdateContentRequest
{
    public string? Slug { get; init; }
    public string? Title { get; init; }
    public string? Excerpt { get; init; }
    public string? Content { get; init; }
    public string? ContentFormat { get; init; }
    public string? Category { get; init; }
    public List<string>? Tags { get; init; }
    public int? DisplayOrder { get; init; }
    public bool? IsFeatured { get; init; }
    public bool? AllowFeedback { get; init; }
    public string? MetaTitle { get; init; }
    public string? MetaDescription { get; init; }
}
