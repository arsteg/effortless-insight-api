using EffortlessInsight.Api.Data.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace EffortlessInsight.Api.Data;

/// <summary>
/// Seeds the initial admin user on application startup if no admin exists.
/// Credentials are read from environment variables for security.
/// </summary>
public class AdminUserSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AdminUserSeeder> _logger;
    private readonly IConfiguration _configuration;

    private const string AdminEmailEnvVar = "ADMIN_EMAIL";
    private const string AdminPasswordEnvVar = "ADMIN_INITIAL_PASSWORD";
    private const string AdminNameEnvVar = "ADMIN_NAME";

    public AdminUserSeeder(
        ApplicationDbContext context,
        ILogger<AdminUserSeeder> logger,
        IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Check if any admin user already exists
        var existingAdmin = await _context.AdminUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(cancellationToken);

        if (existingAdmin != null)
        {
            _logger.LogInformation("Admin user already exists, skipping seed");
            return;
        }

        // Read credentials from environment variables
        var email = Environment.GetEnvironmentVariable(AdminEmailEnvVar)
            ?? _configuration["AdminSeed:Email"];
        var password = Environment.GetEnvironmentVariable(AdminPasswordEnvVar)
            ?? _configuration["AdminSeed:InitialPassword"];
        var name = Environment.GetEnvironmentVariable(AdminNameEnvVar)
            ?? _configuration["AdminSeed:Name"]
            ?? "Administrator";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "No admin user exists and {EmailVar} or {PasswordVar} environment variables are not set. " +
                "Skipping admin seed. Set these variables to create the initial admin user.",
                AdminEmailEnvVar, AdminPasswordEnvVar);
            return;
        }

        // Validate password meets minimum requirements
        var minPasswordLength = _configuration.GetValue("AdminAuth:PasswordMinLength", 16);
        if (password.Length < minPasswordLength)
        {
            _logger.LogError(
                "Admin initial password must be at least {MinLength} characters. Skipping admin seed.",
                minPasswordLength);
            return;
        }

        _logger.LogInformation("Seeding initial admin user with email: {Email}", email);

        var now = DateTime.UtcNow;
        var adminUser = new AdminUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailNormalized = email.ToUpperInvariant(),
            PasswordHash = HashPassword(password),
            Name = name,
            Role = AdminRoles.SuperAdmin,
            Permissions = AdminPermissions.GetDefaultPermissions(AdminRoles.SuperAdmin),
            IsActive = true,
            MustChangePassword = true, // Force password change on first login
            MfaEnabled = false,
            IsLocked = false,
            FailedLoginAttempts = 0,
            CreatedAt = now
        };

        _context.AdminUsers.Add(adminUser);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Initial admin user seeded successfully. Email: {Email}, Role: {Role}. " +
            "Password change will be required on first login.",
            email, AdminRoles.SuperAdmin);
    }

    private static string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
    }
}
