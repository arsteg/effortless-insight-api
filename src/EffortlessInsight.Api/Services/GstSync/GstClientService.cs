using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities.GstSync;
using EffortlessInsight.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Services.GstSync;

/// <summary>
/// Service for managing GST client connections.
/// </summary>
public class GstClientService : IGstClientService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<GstClientService> _logger;

    public GstClientService(ApplicationDbContext context, ILogger<GstClientService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<GstClientDto>> GetClientsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        var clients = await _context.GstClients
            .Include(c => c.CreatedByUser)
            .Where(c => c.OrganizationId == organizationId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return clients.Select(MapToDto).ToList();
    }

    public async Task<GstClientDto?> GetClientByIdAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        return client == null ? null : MapToDto(client);
    }

    public async Task<GstClientDto?> GetClientByGstinAsync(Guid organizationId, string gstin, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Gstin == gstin, cancellationToken);

        return client == null ? null : MapToDto(client);
    }

    public async Task<GstClientDto> CreateClientAsync(Guid organizationId, Guid userId, CreateGstClientRequest request, CancellationToken cancellationToken = default)
    {
        // Check if GSTIN already exists for this organization
        var existing = await _context.GstClients
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Gstin == request.Gstin, cancellationToken);

        if (existing != null)
        {
            throw new InvalidOperationException($"GSTIN {request.Gstin} is already registered for this organization.");
        }

        // Extract state code from GSTIN
        var stateCode = request.Gstin[..2];

        var client = new GstClient
        {
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            Gstin = request.Gstin.ToUpperInvariant(),
            TradeName = request.TradeName,
            LegalName = request.LegalName,
            StateCode = stateCode,
            SyncEnabled = true,
            SyncFrequencyHours = request.SyncFrequencyHours,
            AutoImportToNotices = request.AutoImportToNotices,
            Status = GstClientStatus.PendingFirstSync
        };

        _context.GstClients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created GST client {ClientId} for organization {OrganizationId}, GSTIN: {Gstin}",
            client.Id, organizationId, client.Gstin);

        // Reload with navigation property
        await _context.Entry(client).Reference(c => c.CreatedByUser).LoadAsync(cancellationToken);

        return MapToDto(client);
    }

    public async Task<GstClientDto?> UpdateClientAsync(Guid clientId, UpdateGstClientRequest request, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients
            .Include(c => c.CreatedByUser)
            .FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null)
        {
            return null;
        }

        if (request.TradeName != null)
            client.TradeName = request.TradeName;

        if (request.LegalName != null)
            client.LegalName = request.LegalName;

        if (request.SyncEnabled.HasValue)
            client.SyncEnabled = request.SyncEnabled.Value;

        if (request.SyncFrequencyHours.HasValue)
            client.SyncFrequencyHours = request.SyncFrequencyHours.Value;

        if (request.AutoImportToNotices.HasValue)
            client.AutoImportToNotices = request.AutoImportToNotices.Value;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated GST client {ClientId}", clientId);

        return MapToDto(client);
    }

    public async Task<bool> DeleteClientAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null)
        {
            return false;
        }

        // Soft delete
        client.DeletedAt = DateTime.UtcNow;
        client.Status = GstClientStatus.Disabled;
        client.SyncEnabled = false;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted GST client {ClientId}", clientId);

        return true;
    }

    public async Task<bool> PauseSyncAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null)
        {
            return false;
        }

        client.SyncEnabled = false;
        client.Status = GstClientStatus.Paused;
        client.StatusMessage = "Sync paused by user";

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Paused sync for GST client {ClientId}", clientId);

        return true;
    }

    public async Task<bool> ResumeSyncAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.GstClients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken);

        if (client == null)
        {
            return false;
        }

        client.SyncEnabled = true;
        client.Status = client.LastSuccessfulSyncAt.HasValue ? GstClientStatus.Active : GstClientStatus.PendingFirstSync;
        client.StatusMessage = null;
        client.ConsecutiveFailures = 0;

        // Calculate next sync due time
        client.NextSyncDueAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Resumed sync for GST client {ClientId}", clientId);

        return true;
    }

    private static GstClientDto MapToDto(GstClient client)
    {
        return new GstClientDto
        {
            Id = client.Id,
            Gstin = client.Gstin,
            TradeName = client.TradeName,
            LegalName = client.LegalName,
            StateCode = client.StateCode,
            SyncEnabled = client.SyncEnabled,
            SyncFrequencyHours = client.SyncFrequencyHours,
            AutoImportToNotices = client.AutoImportToNotices,
            Status = client.Status,
            StatusMessage = client.StatusMessage,
            LastSyncAt = client.LastSyncAt,
            LastSyncSource = client.LastSyncSource,
            LastSuccessfulSyncAt = client.LastSuccessfulSyncAt,
            NextSyncDueAt = client.NextSyncDueAt,
            ConsecutiveFailures = client.ConsecutiveFailures,
            TotalNoticesSynced = client.TotalNoticesSynced,
            TotalSyncsPerformed = client.TotalSyncsPerformed,
            CreatedAt = client.CreatedAt,
            CreatedByUserId = client.CreatedByUserId,
            CreatedByUserName = client.CreatedByUser?.Name
        };
    }
}
