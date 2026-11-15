using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;

namespace SiteRoof.Api.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record PageQuery(int Page = 1, int PageSize = 25)
{
    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, 100);
    public int Skip => (SafePage - 1) * SafePageSize;
}

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page.SafePage, page.SafePageSize, total);
    }
}

public static class Versions
{
    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    public static byte[] Decode(string? version, string field = "version")
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw RequestValidationException.For(field, "The current version is required for this change.");
        }
        try
        {
            return Convert.FromBase64String(version);
        }
        catch (FormatException)
        {
            throw RequestValidationException.For(field, "The version token is malformed.");
        }
    }
}

/// <summary>Maps exceptions to RFC 9457 ProblemDetails with a stable machine-readable <c>code</c>.</summary>
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problems, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case RequestValidationException v:
                problem = new ValidationProblemDetails(v.Errors) { Status = v.Status, Title = v.Title };
                problem.Extensions["code"] = v.Code;
                break;
            case AppException app:
                problem = new ProblemDetails { Status = app.Status, Title = app.Title, Detail = app.Message };
                problem.Extensions["code"] = app.Code;
                foreach (var (k, val) in app.Extensions) problem.Extensions[k] = val;
                break;
            case DbUpdateConcurrencyException:
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Conflict",
                    Detail = "This record was changed by someone else. Reload to see the latest version, then try again.",
                };
                problem.Extensions["code"] = "concurrency-conflict";
                break;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = "Bad request", Detail = "The request could not be read." };
                problem.Extensions["code"] = "bad-request";
                break;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", http.Request.Method, http.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Unexpected error",
                    Detail = "Something went wrong on our side. The error has been logged.",
                };
                problem.Extensions["code"] = "server-error";
                break;
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        http.Response.StatusCode = problem.Status ?? 500;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem, Exception = exception });
    }
}
