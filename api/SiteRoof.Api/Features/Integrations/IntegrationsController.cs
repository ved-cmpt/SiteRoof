using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Materials;

namespace SiteRoof.Api.Features.Integrations;

public sealed record OutboxCountsDto(int Pending, int Retrying, int Processing, int Succeeded, int Failed);

public sealed record OutboxEventDto(
    Guid Id,
    string EventType,
    Guid AggregateId,
    string? Description,
    OutboxStatus Status,
    int Attempts,
    DateTimeOffset NextRetryAt,
    string? LastError,
    string? ExternalReference,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt);

public sealed record IntegrationHealthDto(
    string Status,
    bool DispatcherEnabled,
    DateTimeOffset? DispatcherLastRunAt,
    bool MockErpReachable,
    ErpSimulatorMode SimulatorMode,
    int MaxAttempts,
    OutboxCountsDto Outbox,
    IReadOnlyList<OutboxEventDto> Recent);

[ApiController]
[Authorize]
[Route("api/integrations")]
[Produces("application/json")]
public sealed class IntegrationsController(
    SiteRoofDbContext db,
    ICurrentUser user,
    ProjectAccess access,
    IErpClient erp,
    WorkerHeartbeat heartbeat,
    IOptions<OutboxOptions> outboxOptions,
    AuditWriter audit,
    SiteClock clock) : ControllerBase
{
    /// <summary>Outbox counts, recent events, dispatcher heartbeat and mock ERP reachability for this workspace.</summary>
    [HttpGet("health")]
    public async Task<IntegrationHealthDto> Health(CancellationToken ct)
    {
        var tenantId = user.RequireTenantId();
        var counts = await db.IntegrationOutbox.AsNoTracking()
            .GroupBy(o => new { o.Status, Retrying = o.Attempts > 0 })
            .Select(g => new { g.Key.Status, g.Key.Retrying, Count = g.Count() })
            .ToListAsync(ct);
        int Count(OutboxStatus s, bool? retrying = null) =>
            counts.Where(c => c.Status == s && (retrying is null || c.Retrying == retrying)).Sum(c => c.Count);

        var projectIds = access.AccessibleProjectIds();
        var recent = await (
            from o in db.IntegrationOutbox.AsNoTracking()
            join m in db.MaterialRequests.AsNoTracking() on o.AggregateId equals m.Id
            where projectIds.Contains(m.WorkOrder!.ProjectId)
            orderby o.CreatedAt descending
            select new { o, m.Quantity, m.Unit, m.ItemName })
            .Take(20).ToListAsync(ct);
        var recentDtos = recent.Select(x => new OutboxEventDto(x.o.Id, x.o.EventType, x.o.AggregateId,
            $"{x.Quantity:0.##} {x.Unit} {x.ItemName}", x.o.Status, x.o.Attempts, x.o.NextRetryAt, x.o.LastError,
            x.o.ExternalReference, x.o.CreatedAt, x.o.ProcessedAt)).ToList();

        var mode = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.ErpSimulatorMode).SingleAsync(ct);
        var reachable = await erp.PingAsync(ct);
        var opt = outboxOptions.Value;
        var dispatcherHealthy = !opt.DispatcherEnabled ||
            (heartbeat.LastRunAt is { } last && clock.Now - last < TimeSpan.FromSeconds(Math.Max(30, opt.PollIntervalSeconds * 10)));
        var status = !reachable || !dispatcherHealthy ? "degraded" : Count(OutboxStatus.Failed) > 0 ? "attention" : "healthy";

        return new IntegrationHealthDto(status, opt.DispatcherEnabled, heartbeat.LastRunAt, reachable, mode, opt.MaxAttempts,
            new OutboxCountsDto(Count(OutboxStatus.Pending, false), Count(OutboxStatus.Pending, true), Count(OutboxStatus.Processing),
                Count(OutboxStatus.Succeeded), Count(OutboxStatus.Failed)),
            recentDtos);
    }

    /// <summary>Re-queue a failed transmission (project manager). Attempts restart; the idempotency key is unchanged.</summary>
    [HttpPost("outbox/{id:guid}/retry")]
    public async Task<OutboxEventDto> Retry(Guid id, CancellationToken ct)
    {
        var msg = await db.IntegrationOutbox.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("Integration event not found.");
        var mr = await db.MaterialRequests.Include(m => m.WorkOrder).SingleOrDefaultAsync(m => m.Id == msg.AggregateId, ct)
            ?? throw new NotFoundException("Integration event not found.");
        var grant = await access.RequireManagerAsync(mr.WorkOrder!.ProjectId, ct);
        if (msg.Status != OutboxStatus.Failed)
        {
            throw new ConflictException("not-failed", "Only failed transmissions can be retried manually.");
        }
        var before = new Dictionary<string, object?> { ["status"] = msg.Status, ["attempts"] = msg.Attempts };
        msg.Status = OutboxStatus.Pending;
        msg.Attempts = 0;
        msg.NextRetryAt = clock.Now;
        msg.ProcessedAt = null;
        audit.Record(grant.TenantId, mr.WorkOrder.ProjectId, "MaterialRequest", mr.Id, AuditActions.ErpManualRetry,
            $"Re-queued ERP transmission for {mr.Quantity:0.##} {mr.Unit} {mr.ItemName}", before,
            new Dictionary<string, object?> { ["status"] = msg.Status, ["attempts"] = msg.Attempts });
        await db.SaveChangesAsync(ct);
        return new OutboxEventDto(msg.Id, msg.EventType, msg.AggregateId, mr.ItemName, msg.Status, msg.Attempts, msg.NextRetryAt,
            msg.LastError, msg.ExternalReference, msg.CreatedAt, msg.ProcessedAt);
    }
}
