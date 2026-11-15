using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Activity;

public sealed record ActivityLinkDto(string Type, Guid Id);

public sealed record ActivityItemDto(
    long Id,
    DateTimeOffset CreatedAt,
    Guid? ActorId,
    string ActorName,
    string EntityType,
    Guid EntityId,
    string Action,
    string Summary,
    Guid? ProjectId,
    string? ProjectCode,
    JsonElement? Before,
    JsonElement? After,
    ActivityLinkDto? Link);

public sealed class ActivityQuery
{
    public Guid? ProjectId { get; set; }
    public string? EntityType { get; set; }
    public string? Action { get; set; }
    public Guid? EntityId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 30;
}

public sealed class ActivityService(SiteRoofDbContext db, ICurrentUser user, ProjectAccess access)
{
    public const string SystemActor = "SiteRoof integration worker";

    public async Task<PagedResult<ActivityItemDto>> ListAsync(ActivityQuery q, CancellationToken ct)
    {
        if (q.ProjectId is not null) await access.ForProjectAsync(q.ProjectId.Value, ct);
        var ids = access.AccessibleProjectIds();
        var isAdmin = user.IsInRole(TenantRoles.Admin);

        var query = db.AuditEvents.AsNoTracking()
            .Where(a => (a.ProjectId != null && ids.Contains(a.ProjectId.Value)) || (a.ProjectId == null && isAdmin));
        if (q.ProjectId is not null) query = query.Where(a => a.ProjectId == q.ProjectId);
        if (!string.IsNullOrWhiteSpace(q.EntityType)) query = query.Where(a => a.EntityType == q.EntityType);
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action == q.Action);
        if (q.EntityId is not null) query = query.Where(a => a.EntityId == q.EntityId);

        var page = new PageQuery(q.Page, q.PageSize);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(a => new
            {
                a.Id, a.CreatedAt, a.ActorId, ActorName = a.Actor != null ? a.Actor.DisplayName : null,
                a.EntityType, a.EntityId, a.Action, a.Summary, a.ProjectId, a.BeforeJson, a.AfterJson,
            })
            .ToListAsync(ct);

        var projectIds = rows.Where(r => r.ProjectId != null).Select(r => r.ProjectId!.Value).Distinct().ToList();
        var codes = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);

        var logIds = rows.Where(r => r.EntityType == "ProgressLog").Select(r => r.EntityId).ToList();
        var logToWo = await db.ProgressLogs.AsNoTracking().Where(p => logIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.WorkOrderId, ct);
        var mrIds = rows.Where(r => r.EntityType == "MaterialRequest").Select(r => r.EntityId).ToList();
        var mrToWo = await db.MaterialRequests.AsNoTracking().Where(m => mrIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.WorkOrderId, ct);

        var items = rows.Select(r => new ActivityItemDto(
            r.Id, r.CreatedAt, r.ActorId, r.ActorName ?? SystemActor, r.EntityType, r.EntityId, r.Action, r.Summary, r.ProjectId,
            r.ProjectId is { } pid && codes.TryGetValue(pid, out var code) ? code : null,
            Parse(r.BeforeJson), Parse(r.AfterJson),
            r.EntityType switch
            {
                "WorkOrder" => new ActivityLinkDto("workOrder", r.EntityId),
                "ProgressLog" when logToWo.TryGetValue(r.EntityId, out var wo) => new ActivityLinkDto("workOrder", wo),
                "MaterialRequest" when mrToWo.TryGetValue(r.EntityId, out var wo2) => new ActivityLinkDto("workOrder", wo2),
                "Project" or "Milestone" when r.ProjectId is not null => new ActivityLinkDto("project", r.ProjectId.Value),
                _ => null,
            })).ToList();
        return new PagedResult<ActivityItemDto>(items, page.SafePage, page.SafePageSize, total);
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}

[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class ActivityController(ActivityService activity) : ControllerBase
{
    /// <summary>Audit trail across accessible projects (actor, timestamp, before/after of changed fields).</summary>
    [HttpGet("activity")]
    public Task<PagedResult<ActivityItemDto>> List([FromQuery] ActivityQuery q, CancellationToken ct) => activity.ListAsync(q, ct);

    [HttpGet("projects/{id:guid}/activity")]
    public Task<PagedResult<ActivityItemDto>> ForProject(Guid id, [FromQuery] ActivityQuery q, CancellationToken ct)
    {
        q.ProjectId = id;
        return activity.ListAsync(q, ct);
    }
}
