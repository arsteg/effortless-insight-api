using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Tests.Helpers;

public static class MockHelpers
{
    public static Mock<UserManager<ApplicationUser>> CreateMockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var passwordHasher = new Mock<IPasswordHasher<ApplicationUser>>();
        var userValidators = new List<IUserValidator<ApplicationUser>>();
        var passwordValidators = new List<IPasswordValidator<ApplicationUser>>();
        var keyNormalizer = new Mock<ILookupNormalizer>();
        var errors = new Mock<IdentityErrorDescriber>();
        var services = new Mock<IServiceProvider>();
        var logger = new Mock<ILogger<UserManager<ApplicationUser>>>();

        options.Setup(o => o.Value).Returns(new IdentityOptions());
        keyNormalizer.Setup(x => x.NormalizeEmail(It.IsAny<string>()))
            .Returns((string email) => email?.ToUpperInvariant() ?? string.Empty);
        keyNormalizer.Setup(x => x.NormalizeName(It.IsAny<string>()))
            .Returns((string name) => name?.ToUpperInvariant() ?? string.Empty);

        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            options.Object,
            passwordHasher.Object,
            userValidators,
            passwordValidators,
            keyNormalizer.Object,
            errors.Object,
            services.Object,
            logger.Object);
    }

    public static TestDbContext CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    public static Mock<IDistributedCache> CreateMockDistributedCache()
    {
        var cache = new Mock<IDistributedCache>();
        var cacheData = new Dictionary<string, byte[]>();

        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
                cacheData.TryGetValue(key, out var value) ? value : null);

        cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>(
                (key, value, _, _) => cacheData[key] = value)
            .Returns(Task.CompletedTask);

        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => cacheData.Remove(key))
            .Returns(Task.CompletedTask);

        return cache;
    }

    public static Mock<ILogger<T>> CreateMockLogger<T>()
    {
        return new Mock<ILogger<T>>();
    }
}

/// <summary>
/// Simplified test database context that only includes entities needed for auth testing
/// </summary>
public class TestDbContext : ApplicationDbContext
{
    public TestDbContext(DbContextOptions options) : base(new DbContextOptions<ApplicationDbContext>())
    {
        _options = options;
    }

    private readonly DbContextOptions _options;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (_options != null)
        {
            optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString());
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Don't call base - configure only what we need for auth tests
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.UploadedNotices);
            entity.Ignore(e => e.AssignedNotices);
            entity.Ignore(e => e.Comments);
            entity.Ignore(e => e.CreatedTasks);
            entity.Ignore(e => e.AssignedTasks);
            entity.Ignore(e => e.Organization);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<LoginAudit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.User);
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
    }
}
