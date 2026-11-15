using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Data;

namespace SiteRoof.Api.Common;

/// <summary>Per-tenant limits for public guest workspaces so one visitor cannot grow the database unbounded.</summary>
public static class DemoQuotas
{
    public const int MaxProjects = 10;
    public const int MaxWorkOrdersPerProject = 150;
    public const int MaxCommentsPerWorkOrder = 100;
    public const int MaxProgressLogsPerWorkOrder = 100;
    public const int MaxMaterialRequestsPerWorkOrder = 30;

    public static Task<bool> IsDemoTenantAsync(SiteRoofDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.IsDemo, ct);
}
