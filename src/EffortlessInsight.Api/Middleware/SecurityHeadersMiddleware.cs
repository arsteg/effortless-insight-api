namespace EffortlessInsight.Api.Middleware;

/// <summary>
/// Middleware to add security headers to all HTTP responses.
/// Implements OWASP security best practices.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // X-Content-Type-Options: Prevents MIME type sniffing
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // X-Frame-Options: Prevents clickjacking attacks
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // X-XSS-Protection: Legacy XSS protection (still useful for older browsers)
        context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

        // Referrer-Policy: Controls how much referrer info is sent
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Content-Security-Policy: Prevents XSS and data injection attacks
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "frame-ancestors 'none'; " +
            "form-action 'self'; " +
            "base-uri 'self';";

        // Permissions-Policy: Restricts browser features
        context.Response.Headers["Permissions-Policy"] =
            "accelerometer=(), " +
            "camera=(), " +
            "geolocation=(), " +
            "gyroscope=(), " +
            "magnetometer=(), " +
            "microphone=(), " +
            "payment=(), " +
            "usb=()";

        // Strict-Transport-Security: Enforces HTTPS (only in production)
        // HSTS header should only be set if we're running over HTTPS
        if (context.Request.IsHttps)
        {
            // max-age=31536000 (1 year), includeSubDomains, preload
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        // X-Permitted-Cross-Domain-Policies: Prevents Adobe Flash/PDF cross-domain policy files
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";

        // Cache-Control for sensitive endpoints
        if (context.Request.Path.StartsWithSegments("/api/v1/auth") ||
            context.Request.Path.StartsWithSegments("/api/v1/admin"))
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, proxy-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.Headers["Expires"] = "0";
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method to register the security headers middleware.
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
