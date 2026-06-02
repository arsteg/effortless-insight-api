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

        // Configure vector column
        modelBuilder.Entity<Embedding>()
            .Property(e => e.Vector)
            .HasColumnType("vector(3072)");

        // Indexes
        modelBuilder.Entity<Notice>()
            .HasIndex(n => n.OrganizationId);
        modelBuilder.Entity<Notice>()
            .HasIndex(n => n.Status);
        modelBuilder.Entity<Notice>()
            .HasIndex(n => n.ResponseDeadline);
        modelBuilder.Entity<Notice>()
            .HasIndex(n => n.Gstin);

        modelBuilder.Entity<Embedding>()
            .HasIndex(e => e.SourceType);

        // Seed initial data
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
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
                CreatedAt = DateTime.UtcNow
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
                CreatedAt = DateTime.UtcNow
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
                CreatedAt = DateTime.UtcNow
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
                CreatedAt = DateTime.UtcNow
            }
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
