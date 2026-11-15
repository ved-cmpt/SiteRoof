using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Auth;

public static class SiteRoofClaims
{
    public const string TenantId = "siteroof:tenant";
    public const string DisplayName = "siteroof:name";
    public const string Persona = "siteroof:persona";
}

/// <summary>Tenant scope used by EF Core global query filters.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}

public interface ICurrentUser : ITenantContext
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
    string DisplayName { get; }
    bool IsInRole(string role);
    Guid RequireTenantId();
}

/// <summary>
/// Request-scoped identity. Values come from the auth cookie's claims; a request that signs in a
/// different user (demo start, persona switch) calls <see cref="Use"/> so the rest of the request
/// runs as that user.
/// </summary>
public sealed class RequestContext(IHttpContextAccessor accessor) : ICurrentUser
{
    private Guid? _tenantOverride;
    private Guid? _userOverride;
    private string? _nameOverride;
    private string[]? _rolesOverride;

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => _userOverride is not null || Principal?.Identity?.IsAuthenticated == true;

    public Guid? TenantId => _tenantOverride ?? (Guid.TryParse(Principal?.FindFirstValue(SiteRoofClaims.TenantId), out var id) ? id : null);

    public Guid UserId => _userOverride ??
        (Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty);

    public string DisplayName => _nameOverride ?? Principal?.FindFirstValue(SiteRoofClaims.DisplayName) ?? "Unknown";

    public bool IsInRole(string role) => _rolesOverride?.Contains(role) ?? Principal?.IsInRole(role) == true;

    public Guid RequireTenantId() => TenantId ?? throw new ForbiddenException("No workspace is associated with this session.");

    public void Use(AppUser user, IEnumerable<string> roles)
    {
        _tenantOverride = user.TenantId;
        _userOverride = user.Id;
        _nameOverride = user.DisplayName;
        _rolesOverride = roles.ToArray();
    }

    /// <summary>For trusted system work (seeding, background jobs) scoped to one tenant.</summary>
    public void UseSystemTenant(Guid tenantId) => _tenantOverride = tenantId;
}

/// <summary>Adds tenant, display name and persona claims to the Identity cookie.</summary>
public sealed class SiteRoofClaimsFactory(
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(SiteRoofClaims.TenantId, user.TenantId.ToString()));
        identity.AddClaim(new Claim(SiteRoofClaims.DisplayName, user.DisplayName));
        if (user.PersonaKey is not null)
        {
            identity.AddClaim(new Claim(SiteRoofClaims.Persona, user.PersonaKey));
        }
        return identity;
    }
}
