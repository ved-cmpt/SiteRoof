using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Auth;

public sealed record SessionTenantDto(Guid Id, string Name, bool IsDemo, DateTimeOffset? ExpiresAt, ErpSimulatorMode ErpSimulatorMode);

public sealed record SessionUserDto(Guid Id, string DisplayName, string? JobTitle, IReadOnlyList<string> Roles, string? Persona, SessionTenantDto Tenant);

public sealed record SessionDto(bool Authenticated, SessionUserDto? User);

/// <summary>
/// Cookie sign-in plus CSRF token issuance. Angular reads the non-HttpOnly <c>XSRF-TOKEN</c> cookie and
/// echoes it in the <c>X-XSRF-TOKEN</c> header on unsafe requests; the antiforgery token is bound to the
/// signed-in user, so it is re-issued whenever the identity changes.
/// </summary>
public sealed class SessionService(
    SiteRoofDbContext db,
    SignInManager<AppUser> signIn,
    UserManager<AppUser> users,
    IAntiforgery antiforgery,
    IHttpContextAccessor accessor,
    RequestContext requestContext)
{
    public const string CsrfCookie = "XSRF-TOKEN";

    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("No HTTP context.");

    public async Task<SessionDto> SignInAsync(AppUser user)
    {
        await signIn.SignInAsync(user, isPersistent: false);
        var principal = await signIn.CreateUserPrincipalAsync(user);
        Http.User = principal;
        var roles = await users.GetRolesAsync(user);
        requestContext.Use(user, roles);
        IssueCsrfToken();
        return await DescribeAsync(user, roles);
    }

    public async Task SignOutAsync()
    {
        await signIn.SignOutAsync();
        Http.User = new ClaimsPrincipal(new ClaimsIdentity());
        IssueCsrfToken();
    }

    public async Task<SessionDto> CurrentAsync()
    {
        IssueCsrfToken();
        if (Http.User.Identity?.IsAuthenticated != true) return new SessionDto(false, null);
        var id = Http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null)
        {
            // The workspace behind this cookie was deleted (expired demo or reset).
            await SignOutAsync();
            return new SessionDto(false, null);
        }
        return await DescribeAsync(user, await users.GetRolesAsync(user));
    }

    public void IssueCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(Http);
        Http.Response.Cookies.Append(CsrfCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = Http.Request.IsHttps,
            Path = "/",
            IsEssential = true,
        });
    }

    private async Task<SessionDto> DescribeAsync(AppUser user, IList<string> roles)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == user.TenantId);
        return new SessionDto(true, new SessionUserDto(user.Id, user.DisplayName, user.JobTitle, roles.Order().ToList(), user.PersonaKey,
            new SessionTenantDto(tenant.Id, tenant.Name, tenant.IsDemo, tenant.ExpiresAt, tenant.ErpSimulatorMode)));
    }
}
