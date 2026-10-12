using System.Security.Cryptography;
using System.Text.Json;
using EffortlessInsight.Api.Services.Encryption;
using Microsoft.AspNetCore.WebUtilities;
using StackExchange.Redis;

namespace EffortlessInsight.Api.Services.EmailSync;

public record EmailOAuthAttempt(Guid OrganizationId, Guid UserId, string Provider,
    string CallbackUri, string ReturnUri, string Verifier);

public interface IEmailOAuthAttemptStore
{
    Task<string> CreateAsync(EmailOAuthAttempt attempt);
    Task<EmailOAuthAttempt?> ConsumeAsync(string state);
}

// Shared across API instances. GET+DEL is atomic; verifier and context are encrypted.
public sealed class EmailOAuthAttemptStore(IConnectionMultiplexer redis, IFieldEncryptionService encryption)
    : IEmailOAuthAttemptStore
{
    public async Task<string> CreateAsync(EmailOAuthAttempt attempt)
    {
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await redis.GetDatabase().StringSetAsync("email:oauth:" + state,
            encryption.Encrypt(JsonSerializer.Serialize(attempt)), TimeSpan.FromMinutes(10));
        return state;
    }

    public async Task<EmailOAuthAttempt?> ConsumeAsync(string state)
    {
        if (state.Length != 43 || state.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) return null;
        var value = await redis.GetDatabase().ScriptEvaluateAsync(
            "local v=redis.call('GET',KEYS[1]); if v then redis.call('DEL',KEYS[1]); end; return v",
            [new RedisKey("email:oauth:" + state)]);
        return value.IsNull ? null : JsonSerializer.Deserialize<EmailOAuthAttempt>(encryption.Decrypt((string)value!));
    }
}
