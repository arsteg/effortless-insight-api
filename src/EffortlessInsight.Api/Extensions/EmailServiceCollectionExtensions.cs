using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using EffortlessInsight.Api.Services.Email;

namespace EffortlessInsight.Api.Extensions;

/// <summary>
/// Registers the Amazon SES email infrastructure.
/// </summary>
public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddSesEmailServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // One SES client for the process lifetime; AWS clients are thread-safe
        // and expensive to create. Standard retry mode handles throttling.
        services.AddSingleton<IAmazonSimpleEmailServiceV2>(_ =>
        {
            var awsSection = configuration.GetSection("AWS");
            var region = configuration[$"{EmailOptions.SectionName}:AwsRegion"]
                         ?? awsSection["Region"]
                         ?? "ap-south-1";

            var config = new AmazonSimpleEmailServiceV2Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(region)
            };

            // Dedicated email credentials win; shared AWS section is the fallback.
            var accessKeyId = configuration[$"{EmailOptions.SectionName}:AwsAccessKeyId"];
            var secretAccessKey = configuration[$"{EmailOptions.SectionName}:AwsSecretAccessKey"];
            if (string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
            {
                accessKeyId = awsSection["AccessKeyId"];
                secretAccessKey = awsSection["SecretAccessKey"];
            }
            if (!string.IsNullOrEmpty(accessKeyId) && !string.IsNullOrEmpty(secretAccessKey))
            {
                // Explicit credentials from configuration (local development)
                return new AmazonSimpleEmailServiceV2Client(
                    new BasicAWSCredentials(accessKeyId, secretAccessKey), config);
            }

            // Default AWS credential chain: environment variables, shared
            // profile, ECS task role, EC2/Beanstalk instance profile.
            return new AmazonSimpleEmailServiceV2Client(config);
        });

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddSingleton<IEmailService, AmazonSesEmailService>();

        return services;
    }
}
