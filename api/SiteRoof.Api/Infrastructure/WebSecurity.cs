using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SiteRoof.Api.Features.Demo;

namespace SiteRoof.Api.Infrastructure;

public static class RateLimits
{
    public const string DemoProvision = "demo-provision";
    public const string Auth = "auth";

    public static IServiceCollection AddSiteRoofRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var demo = config.GetSection(DemoOptions.Section).Get<DemoOptions>() ?? new DemoOptions();
        var apiPerMinute = config.GetValue("RateLimits:ApiPermitsPerMinute", 600);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                {
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
                }
                await Results.Problem(statusCode: 429, title: "Too many requests",
                        detail: "You're doing that too often. Wait a moment and try again.",
                        extensions: new Dictionary<string, object?> { ["code"] = "rate-limited" })
                    .ExecuteAsync(ctx.HttpContext);
            };

            // Global per-client limit on API traffic.
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                http.Request.Path.StartsWithSegments("/api")
                    ? RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = apiPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("static"));

            // Creating a guest workspace is the expensive operation: a few per client per window.
            o.AddPolicy(DemoProvision, http => RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = demo.ProvisionPermitsPerWindow, Window = TimeSpan.FromMinutes(demo.ProvisionWindowMinutes), QueueLimit = 0,
            }));

            o.AddPolicy(Auth, http => RateLimitPartition.GetFixedWindowLimiter(ClientKey(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            }));
        });
        return services;
    }

    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>
/// Validates the antiforgery token on every unsafe /api request (cookie-authenticated API, so CSRF must be
/// enforced server-side). The self-hosted mock ERP uses a shared key instead and lives outside /api.
/// </summary>
public sealed class CsrfValidationMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task InvokeAsync(HttpContext http, IAntiforgery antiforgery)
    {
        if (http.Request.Path.StartsWithSegments("/api") && !SafeMethods.Contains(http.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                await http.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = 400,
                    Title = "Security token missing or invalid",
                    Detail = "Refresh the page and try again.",
                    Extensions = { ["code"] = "csrf-invalid" },
                }, options: null, contentType: "application/problem+json");
                return;
            }
        }
        await next(http);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    private const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext http)
    {
        var headers = http.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        // The interactive API reference (development only) loads its UI bundle from a CDN.
        if (!(env.IsDevelopment() && http.Request.Path.StartsWithSegments("/scalar")))
        {
            headers["Content-Security-Policy"] = Csp;
        }
        return next(http);
    }
}

/// <summary>One structured log line per request: method, path, status, elapsed ms, user and trace id.</summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (!http.Request.Path.StartsWithSegments("/api") && !http.Request.Path.StartsWithSegments("/mock-erp"))
        {
            await next(http);
            return;
        }
        var sw = Stopwatch.StartNew();
        try
        {
            await next(http);
        }
        finally
        {
            sw.Stop();
            var level = http.Response.StatusCode >= 500 ? LogLevel.Error : http.Response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information;
            logger.Log(level, "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs} ms (user {UserId}, trace {TraceId})",
                http.Request.Method, http.Request.Path.Value, http.Response.StatusCode, sw.ElapsedMilliseconds,
                http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
                Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier);
        }
    }
}
