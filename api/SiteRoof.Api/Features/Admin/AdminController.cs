using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Admin;

public sealed record AdminUserDto(Guid Id, string DisplayName, string? JobTitle, IReadOnlyList<string> Roles, string? Persona,
    IReadOnlyList<string> Projects);

public sealed record TenantSettingsDto(Guid Id, string Name, bool IsDemo, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt,
    ErpSimulatorMode ErpSimulatorMode, IReadOnlyList<AdminUserDto> Users, TenantUsageDto Usage);

public sealed record TenantUsageDto(int Projects, int WorkOrders, int ProgressLogs, int MaterialRequests, int AuditEvents,
    int MaxProjects, int MaxWorkOrdersPerProject);

public sealed class UpdateTenantRequest
{
    [StringLength(120, MinimumLength = 3)] public string? Name { get; set; }
    public ErpSimulatorMode? ErpSimulatorMode { get; set; }
}

[ApiController]
[Authorize(Roles = TenantRoles.Admin)]
[Route("api/admin")]
[Produces("application/json")]
public sealed class AdminController(SiteRoofDbContext db, ICurrentUser user, AuditWriter audit) : ControllerBase
{
    [HttpGet("tenant")]
    public async Task<TenantSettingsDto> Get(CancellationToken ct)
    {
        var tenantId = user.RequireTenantId();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, ct);
        var users = await db.Users.AsNoTracking().Where(u => u.TenantId == tenantId).OrderBy(u => u.DisplayName).ToListAsync(ct);
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           join u in db.Users on ur.UserId equals u.Id
                           where u.TenantId == tenantId
                           select new { ur.UserId, r.Name }).ToListAsync(ct);
        var memberships = await db.ProjectMemberships.AsNoTracking()
            .Select(m => new { m.UserId, m.Project!.Code, m.Role }).ToListAsync(ct);

        var usage = new TenantUsageDto(
            await db.Projects.CountAsync(ct), await db.WorkOrders.CountAsync(ct), await db.ProgressLogs.CountAsync(ct),
            await db.MaterialRequests.CountAsync(ct), await db.AuditEvents.CountAsync(ct),
            DemoQuotas.MaxProjects, DemoQuotas.MaxWorkOrdersPerProject);

        return new TenantSettingsDto(tenant.Id, tenant.Name, tenant.IsDemo, tenant.CreatedAt, tenant.ExpiresAt, tenant.ErpSimulatorMode,
            users.Select(u => new AdminUserDto(u.Id, u.DisplayName, u.JobTitle,
                roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).Order().ToList(), u.PersonaKey,
                memberships.Where(m => m.UserId == u.Id).OrderBy(m => m.Code).Select(m => $"{m.Code} · {m.Role}").ToList())).ToList(),
            usage);
    }

    [HttpPatch("tenant")]
    public async Task<TenantSettingsDto> Update(UpdateTenantRequest req, CancellationToken ct)
    {
        var tenantId = user.RequireTenantId();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, ct);
        var before = new Dictionary<string, object?> { ["name"] = tenant.Name, ["erpSimulatorMode"] = tenant.ErpSimulatorMode };
        if (req.Name is not null) tenant.Name = req.Name.Trim();
        if (req.ErpSimulatorMode is not null) tenant.ErpSimulatorMode = req.ErpSimulatorMode.Value;
        var after = new Dictionary<string, object?> { ["name"] = tenant.Name, ["erpSimulatorMode"] = tenant.ErpSimulatorMode };
        if (AuditWriter.Diff(before, after).After is { Count: > 0 })
        {
            audit.Record(tenantId, null, "Tenant", tenantId, AuditActions.SettingsChanged, "Changed workspace settings", before, after);
            await db.SaveChangesAsync(ct);
        }
        return await Get(ct);
    }
}
