using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Integrations;

/// <summary>Last-run information for the health endpoint.</summary>
public sealed class WorkerHeartbeat
{
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int Processed { get; set; }
}

public sealed record OutboxRunResult(int Claimed, int Succeeded, int Retried, int Failed);

/// <summary>
/// Claims due outbox rows and delivers them to the ERP adapter. Claiming uses
/// <c>UPDATE ... OUTPUT</c> with <c>READPAST, UPDLOCK</c> so two dispatchers never deliver the same
/// row concurrently. Delivery is at-least-once; the idempotency key makes it effectively-once at the ERP.
/// </summary>
public sealed class OutboxProcessor(
    SiteRoofDbContext db,
    IErpClient erp,
    AuditWriter audit,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    WorkerHeartbeat heartbeat,
    ILogger<OutboxProcessor> logger)
{
    public async Task<OutboxRunResult> ProcessDueAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var now = time.GetUtcNow();
        var lockUntil = now.AddSeconds(opt.LockSeconds);
        heartbeat.LastRunAt = now;

        var claimed = await db.Database.SqlQuery<Guid>($"""
            WITH due AS (
                SELECT TOP ({opt.BatchSize}) Id, Status, LockedUntil
                FROM dbo.IntegrationOutbox WITH (ROWLOCK, READPAST, UPDLOCK)
                WHERE (Status = 'Pending' AND NextRetryAt <= {now})
                   OR (Status = 'Processing' AND LockedUntil < {now})
                ORDER BY NextRetryAt
            )
            UPDATE due SET Status = 'Processing', LockedUntil = {lockUntil}
            OUTPUT inserted.Id AS [Value];
            """).ToListAsync(ct);

        int ok = 0, retried = 0, failed = 0;
        foreach (var id in claimed)
        {
            var outcome = await DeliverAsync(id, ct);
            switch (outcome)
            {
                case OutboxStatus.Succeeded: ok++; break;
                case OutboxStatus.Failed: failed++; break;
                default: retried++; break;
            }
            db.ChangeTracker.Clear();
        }

        heartbeat.Processed += claimed.Count;
        if (claimed.Count > 0 && failed == 0 && retried == 0) heartbeat.LastSuccessAt = time.GetUtcNow();
        return new OutboxRunResult(claimed.Count, ok, retried, failed);
    }

    private async Task<OutboxStatus> DeliverAsync(Guid id, CancellationToken ct)
    {
        var opt = options.Value;
        var msg = await db.IntegrationOutbox.IgnoreQueryFilters().SingleAsync(o => o.Id == id, ct);
        var mr = await db.MaterialRequests.IgnoreQueryFilters().Include(m => m.WorkOrder)
            .SingleOrDefaultAsync(m => m.Id == msg.AggregateId, ct);

        var result = await erp.SendRequisitionAsync(msg.IdempotencyKey, msg.Payload, ct);
        var now = time.GetUtcNow();
        msg.Attempts++;
        msg.LockedUntil = null;
        var projectId = mr?.WorkOrder?.ProjectId;
        var label = mr is null ? "material request" : $"{mr.Quantity:0.##} {mr.Unit} {mr.ItemName}";

        switch (result)
        {
            case ErpResult.Success s:
                msg.Status = OutboxStatus.Succeeded;
                msg.ExternalReference = s.ExternalReference;
                msg.ProcessedAt = now;
                msg.LastError = null;
                if (mr is not null) mr.ExternalReference = s.ExternalReference;
                audit.Record(msg.TenantId, projectId, "MaterialRequest", msg.AggregateId, AuditActions.ErpAcknowledged,
                    $"ERP acknowledged requisition for {label} as {s.ExternalReference} (attempt {msg.Attempts})",
                    new Dictionary<string, object?> { ["erp"] = "pending" },
                    new Dictionary<string, object?> { ["erp"] = "sent", ["externalReference"] = s.ExternalReference, ["attempts"] = msg.Attempts });
                break;

            case ErpResult.Transient t when msg.Attempts < opt.MaxAttempts:
                var delay = opt.BackoffFor(msg.Attempts, Random.Shared.NextDouble());
                msg.Status = OutboxStatus.Pending;
                msg.LastError = t.Error;
                msg.NextRetryAt = now + delay;
                audit.Record(msg.TenantId, projectId, "MaterialRequest", msg.AggregateId, AuditActions.ErpRetryScheduled,
                    $"ERP returned {t.Error} for {label}; retry {msg.Attempts + 1} of {opt.MaxAttempts} in {delay.TotalSeconds:0}s",
                    after: new Dictionary<string, object?> { ["attempts"] = msg.Attempts, ["error"] = t.Error, ["nextRetryAt"] = msg.NextRetryAt });
                logger.LogWarning("Outbox {OutboxId} attempt {Attempt} failed: {Error}; retrying at {NextRetryAt}", msg.Id, msg.Attempts, t.Error, msg.NextRetryAt);
                break;

            default:
                var error = result switch { ErpResult.Transient t => t.Error, ErpResult.Permanent p => p.Error, _ => "Unknown" };
                msg.Status = OutboxStatus.Failed;
                msg.LastError = error;
                msg.ProcessedAt = now;
                audit.Record(msg.TenantId, projectId, "MaterialRequest", msg.AggregateId, AuditActions.ErpFailed,
                    $"ERP transmission for {label} failed after {msg.Attempts} attempt(s): {error}",
                    after: new Dictionary<string, object?> { ["attempts"] = msg.Attempts, ["error"] = error });
                logger.LogError("Outbox {OutboxId} failed permanently after {Attempts} attempts: {Error}", msg.Id, msg.Attempts, error);
                heartbeat.LastError = error;
                break;
        }

        await db.SaveChangesAsync(ct);
        return msg.Status;
    }
}

/// <summary>Background service that polls the outbox on a fixed interval.</summary>
public sealed class OutboxDispatcher(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, WorkerHeartbeat heartbeat, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DispatcherEnabled)
        {
            logger.LogInformation("Outbox dispatcher disabled by configuration");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var result = await processor.ProcessDueAsync(stoppingToken);
                if (result.Claimed > 0)
                {
                    logger.LogInformation("Outbox run: {Claimed} claimed, {Succeeded} sent, {Retried} retrying, {Failed} failed",
                        result.Claimed, result.Succeeded, result.Retried, result.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                heartbeat.LastError = ex.Message;
                logger.LogError(ex, "Outbox dispatcher run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
