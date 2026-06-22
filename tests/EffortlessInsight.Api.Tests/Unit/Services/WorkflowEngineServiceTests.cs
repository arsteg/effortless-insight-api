using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Features.Workflows.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace EffortlessInsight.Api.Tests.Unit.Services;

public class WorkflowEngineServiceTests
{
    private readonly Mock<ILogger<WorkflowEngineService>> _loggerMock;

    public WorkflowEngineServiceTests()
    {
        _loggerMock = new Mock<ILogger<WorkflowEngineService>>();
    }

    private ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetWorkflowTemplatesAsync_ReturnsOrganizationTemplates()
    {
        // Arrange
        using var context = CreateDbContext();
        var orgId = Guid.NewGuid();

        var template1 = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Default Workflow",
            Description = "Default workflow for notices",
            IsDefault = true,
            IsActive = true,
            Version = 1
        };
        var template2 = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Custom Workflow",
            Description = "Custom workflow",
            IsDefault = false,
            IsActive = true,
            Version = 1
        };
        context.WorkflowTemplates.AddRange(template1, template2);
        await context.SaveChangesAsync();

        var service = new WorkflowEngineService(context, _loggerMock.Object);

        // Act
        var templates = await service.GetWorkflowTemplatesAsync(orgId);

        // Assert
        Assert.Equal(2, templates.Count);
        Assert.Contains(templates, t => t.Name == "Default Workflow");
        Assert.Contains(templates, t => t.Name == "Custom Workflow");
    }

    [Fact]
    public async Task CreateWorkflowTemplateAsync_CreatesTemplate()
    {
        // Arrange
        using var context = CreateDbContext();
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var service = new WorkflowEngineService(context, _loggerMock.Object);

        // Act
        var template = await service.CreateWorkflowTemplateAsync(
            orgId,
            "New Workflow",
            "A new workflow",
            false,
            userId);

        // Assert
        Assert.NotNull(template);
        Assert.Equal("New Workflow", template.Name);
        Assert.Equal(orgId, template.OrganizationId);
        Assert.False(template.IsDefault);
        Assert.True(template.IsActive);

        var dbTemplate = await context.WorkflowTemplates.FindAsync(template.Id);
        Assert.NotNull(dbTemplate);
    }

    [Fact]
    public async Task StartWorkflowAsync_CreatesWorkflowInstance()
    {
        // Arrange
        using var context = CreateDbContext();
        var orgId = Guid.NewGuid();
        var noticeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var template = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Test Workflow",
            IsDefault = true,
            IsActive = true,
            Version = 1
        };
        var stage1 = new WorkflowStage
        {
            Id = Guid.NewGuid(),
            WorkflowTemplateId = template.Id,
            Name = "Initial Review",
            Order = 1,
            IsInitial = true,
            IsFinal = false
        };
        var stage2 = new WorkflowStage
        {
            Id = Guid.NewGuid(),
            WorkflowTemplateId = template.Id,
            Name = "Completed",
            Order = 2,
            IsInitial = false,
            IsFinal = true
        };

        context.WorkflowTemplates.Add(template);
        context.WorkflowStages.AddRange(stage1, stage2);
        await context.SaveChangesAsync();

        var service = new WorkflowEngineService(context, _loggerMock.Object);

        // Act
        var instance = await service.StartWorkflowAsync(noticeId, orgId, template.Id, userId);

        // Assert
        Assert.NotNull(instance);
        Assert.Equal(noticeId, instance.NoticeId);
        Assert.Equal(template.Id, instance.WorkflowTemplateId);
        Assert.Equal(stage1.Id, instance.CurrentStageId);
        Assert.Equal("active", instance.Status);
    }

    [Fact]
    public async Task TransitionWorkflowAsync_MovesToNextStage()
    {
        // Arrange
        using var context = CreateDbContext();
        var orgId = Guid.NewGuid();
        var noticeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var template = new WorkflowTemplate
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            Name = "Test Workflow",
            IsDefault = true,
            IsActive = true,
            Version = 1
        };
        var stage1 = new WorkflowStage
        {
            Id = Guid.NewGuid(),
            WorkflowTemplateId = template.Id,
            Name = "Initial Review",
            Order = 1,
            IsInitial = true,
            IsFinal = false
        };
        var stage2 = new WorkflowStage
        {
            Id = Guid.NewGuid(),
            WorkflowTemplateId = template.Id,
            Name = "Completed",
            Order = 2,
            IsInitial = false,
            IsFinal = true
        };

        var instance = new NoticeWorkflowInstance
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            OrganizationId = orgId,
            WorkflowTemplateId = template.Id,
            CurrentStageId = stage1.Id,
            Status = "active",
            StartedAt = DateTime.UtcNow,
            StartedById = userId
        };

        context.WorkflowTemplates.Add(template);
        context.WorkflowStages.AddRange(stage1, stage2);
        context.NoticeWorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        var service = new WorkflowEngineService(context, _loggerMock.Object);

        // Act
        var result = await service.TransitionWorkflowAsync(
            instance.Id,
            stage2.Id,
            userId,
            "Moving to next stage",
            null);

        // Assert
        Assert.True(result.Success);

        var updatedInstance = await context.NoticeWorkflowInstances.FindAsync(instance.Id);
        Assert.Equal(stage2.Id, updatedInstance!.CurrentStageId);

        var history = await context.WorkflowHistories
            .Where(h => h.WorkflowInstanceId == instance.Id)
            .ToListAsync();
        Assert.Single(history);
        Assert.Equal(stage1.Id, history[0].FromStageId);
        Assert.Equal(stage2.Id, history[0].ToStageId);
    }

    [Fact]
    public async Task GetWorkflowInstanceAsync_ReturnsInstance()
    {
        // Arrange
        using var context = CreateDbContext();
        var instanceId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var noticeId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var stageId = Guid.NewGuid();

        var instance = new NoticeWorkflowInstance
        {
            Id = instanceId,
            NoticeId = noticeId,
            OrganizationId = orgId,
            WorkflowTemplateId = templateId,
            CurrentStageId = stageId,
            Status = "active",
            StartedAt = DateTime.UtcNow
        };
        context.NoticeWorkflowInstances.Add(instance);
        await context.SaveChangesAsync();

        var service = new WorkflowEngineService(context, _loggerMock.Object);

        // Act
        var result = await service.GetWorkflowInstanceAsync(instanceId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(instanceId, result.Id);
        Assert.Equal(noticeId, result.NoticeId);
        Assert.Equal("active", result.Status);
    }
}
