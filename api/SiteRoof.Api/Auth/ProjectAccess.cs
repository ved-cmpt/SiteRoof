using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Auth;

/// <summary>The caller's effective access to one project.</summary>
public sealed record ProjectAccessGrant(Guid ProjectId, Guid TenantId, ProjectRole? MembershipRole, bool IsTenantAdmin)
{
    public bool CanManage => MembershipRole == ProjectRole.Manager;
    public bool CanSupervise => MembershipRole is ProjectRole.Manager or ProjectRole.Supervisor;
    public bool IsMember => MembershipRole is not null;
}

/// <summary>
/// Object-level authorization for everything inside a project. Every service call that reads or
/// writes project data resolves access here first. Cross-tenant ids are invisible (404) because
/// of the tenant query filter; same-tenant projects without membership return 403.
/// </summary>
public sealed class ProjectAccess(SiteRoofDbContext db, ICurrentUser user)
{
    private readonly Dictionary<Guid, ProjectAccessGrant> _cache = [];

    public async Task<ProjectAccessGrant> ForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated) throw new ForbiddenException("Sign in to continue.");
        if (_cache.TryGetValue(projectId, out var cached)) return cached;

        var project = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.TenantId })
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Project not found.");

        var role = await db.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == user.UserId)
            .Select(m => (ProjectRole?)m.Role)
            .SingleOrDefaultAsync(ct);

        var isAdmin = user.IsInRole(TenantRoles.Admin);
        if (role is null && !isAdmin)
        {
            throw new ForbiddenException("You are not a member of this project.");
        }

        var grant = new ProjectAccessGrant(project.Id, project.TenantId, role, isAdmin);
        _cache[projectId] = grant;
        return grant;
    }

    public async Task<ProjectAccessGrant> RequireManagerAsync(Guid projectId, CancellationToken ct = default)
    {
        var grant = await ForProjectAsync(projectId, ct);
        if (!grant.CanManage) throw new ForbiddenException("Only the project manager can perform this action.");
        return grant;
    }

    public async Task<ProjectAccessGrant> RequireSupervisorAsync(Guid projectId, CancellationToken ct = default)
    {
        var grant = await ForProjectAsync(projectId, ct);
        if (!grant.CanSupervise) throw new ForbiddenException("Only a project manager or site supervisor can perform this action.");
        return grant;
    }

    public async Task<ProjectAccessGrant> RequireMemberAsync(Guid projectId, CancellationToken ct = default)
    {
        var grant = await ForProjectAsync(projectId, ct);
        if (!grant.IsMember) throw new ForbiddenException("Only project members can perform this action.");
        return grant;
    }

    /// <summary>Ids of projects the caller can read (membership, or every tenant project for Admins).</summary>
    public IQueryable<Guid> AccessibleProjectIds()
    {
        if (user.IsInRole(TenantRoles.Admin))
        {
            return db.Projects.Select(p => p.Id);
        }
        var uid = user.UserId;
        return db.ProjectMemberships.Where(m => m.UserId == uid).Select(m => m.ProjectId);
    }
}
