using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Integrations;
using SiteRoof.Api.Features.WorkOrders;

namespace SiteRoof.Api.Features.Materials;

public enum ErpTransmissionStatus { NotApplicable, Queued, Retrying, Sent, Failed }

public sealed record ErpStatusDto(
    ErpTransmissionStatus Status,
    int Attempts,
    DateTimeOffset? NextRetryAt,
    string? LastError,
    string? ExternalReference,
    DateTimeOffset? ProcessedAt,
    Guid? OutboxId);

public sealed record MaterialRequestDto(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    Guid WorkOrderId,
    string WorkOrderKey,
    string WorkOrderTitle,
    string ItemName,
    decimal Quantity,
    string Unit,
    string? Justification,
    MaterialRequestStatus Status,
    UserRefDto RequestedBy,
    DateTimeOffset RequestedAt,
    UserRefDto? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    string? ExternalReference,
    string Version,
    bool CanDecide,
    ErpStatusDto Erp);

public sealed class CreateMaterialRequest
{
    /// <summary>Client-generated id; re-sending the same id returns the original request instead of a duplicate.</summary>
    [Required] public Guid? ClientRequestId { get; set; }
    [Required, StringLength(160, MinimumLength = 2)] public string ItemName { get; set; } = "";
    [Range(0.01, 1_000_000, ErrorMessage = "Quantity must be greater than zero.")] public decimal Quantity { get; set; }
    [Required, StringLength(16, MinimumLength = 1)] public string Unit { get; set; } = "";
    [StringLength(1000)] public string? Justification { get; set; }
}

public enum MaterialDecision { Approve, Reject }

public sealed class DecisionRequest
{
    [Required] public MaterialDecision? Decision { get; set; }
    [StringLength(1000)] public string? Note { get; set; }
    /// <summary>Optional optimistic-concurrency version from the list or detail response.</summary>
    public string? Version { get; set; }
}

public sealed class MaterialRequestQuery
{
    public MaterialRequestStatus? Status { get; set; }
    public Guid? ProjectId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed record MaterialSubmitResult(MaterialRequestDto Request, bool Created);

public sealed class MaterialRequestService(
    SiteRoofDbContext db,
    ICurrentUser user,
    ProjectAccess access,
    AuditWriter audit,
    SiteClock clock,
    WorkOrderService workOrders)
{
    public const string ApprovedEventType = "MaterialRequestApproved";

    public async Task<PagedResult<MaterialRequestDto>> ListAsync(MaterialRequestQuery q, CancellationToken ct)
    {
        var ids = access.AccessibleProjectIds();
        var query = db.MaterialRequests.AsNoTracking().Where(m => ids.Contains(m.WorkOrder!.ProjectId));
        if (q.ProjectId is not null) query = query.Where(m => m.WorkOrder!.ProjectId == q.ProjectId);
        if (q.Status is not null) query = query.Where(m => m.Status == q.Status);
        query = query.OrderBy(m => m.Status != MaterialRequestStatus.Submitted).ThenByDescending(m => m.DecidedAt ?? m.RequestedAt);

        var page = new PageQuery(q.Page, q.PageSize);
        var total = await query.CountAsync(ct);
        var ids2 = await query.Skip(page.Skip).Take(page.SafePageSize).Select(m => m.Id).ToListAsync(ct);
        var items = await ProjectAsync(ids2, ct);
        return new PagedResult<MaterialRequestDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<IReadOnlyList<MaterialRequestDto>> ListForWorkOrderAsync(Guid workOrderId, CancellationToken ct)
    {
        await workOrders.LoadAsync(workOrderId, tracking: false, ct);
        var ids = await db.MaterialRequests.AsNoTracking().Where(m => m.WorkOrderId == workOrderId)
            .OrderByDescending(m => m.RequestedAt).Select(m => m.Id).ToListAsync(ct);
        return await ProjectAsync(ids, ct);
    }

    public async Task<MaterialSubmitResult> SubmitAsync(Guid workOrderId, CreateMaterialRequest req, CancellationToken ct)
    {
        var (wo, grant) = await workOrders.LoadAsync(workOrderId, tracking: false, ct);
        var isAssignee = grant.IsMember && wo.AssigneeId == user.UserId;
        if (!isAssignee && !grant.CanSupervise)
        {
            throw new ForbiddenException("Only the assigned crew member or a supervisor can request materials for this work order.");
        }

        // Idempotent submit: same client id for the same work order returns the original request.
        var existing = await db.MaterialRequests.AsNoTracking()
            .Where(m => m.WorkOrderId == workOrderId && m.ClientRequestId == req.ClientRequestId)
            .Select(m => m.Id).SingleOrDefaultAsync(ct);
        if (existing != Guid.Empty) return new MaterialSubmitResult((await ProjectAsync([existing], ct)).Single(), false);

        if (!WorkOrderStateMachine.IsActive(wo.Status))
        {
            throw new BusinessRuleException("work-order-not-active", "Materials can only be requested for open, in-progress or blocked work.");
        }
        if (await DemoQuotas.IsDemoTenantAsync(db, grant.TenantId, ct)
            && await db.MaterialRequests.CountAsync(m => m.WorkOrderId == workOrderId, ct) >= DemoQuotas.MaxMaterialRequestsPerWorkOrder)
        {
            throw new BusinessRuleException("quota-exceeded", "This demo work order has reached its material-request limit.");
        }

        var mr = new MaterialRequest
        {
            TenantId = grant.TenantId,
            WorkOrderId = workOrderId,
            ItemName = req.ItemName.Trim(),
            Quantity = req.Quantity,
            Unit = req.Unit.Trim(),
            Justification = string.IsNullOrWhiteSpace(req.Justification) ? null : req.Justification.Trim(),
            Status = MaterialRequestStatus.Submitted,
            RequestedById = user.UserId,
            RequestedAt = clock.Now,
            ClientRequestId = req.ClientRequestId!.Value,
        };
        db.MaterialRequests.Add(mr);
        var code = await db.Projects.Where(p => p.Id == wo.ProjectId).Select(p => p.Code).SingleAsync(ct);
        audit.Record(grant.TenantId, wo.ProjectId, "MaterialRequest", mr.Id, AuditActions.Submitted,
            $"Requested {mr.Quantity:0.##} {mr.Unit} {mr.ItemName} for {WorkOrderService.Key(code, wo.Number)}",
            after: new Dictionary<string, object?>
            {
                ["item"] = mr.ItemName, ["quantity"] = mr.Quantity, ["unit"] = mr.Unit, ["status"] = mr.Status, ["justification"] = mr.Justification,
            });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (WorkOrderService.IsUniqueViolation(ex))
        {
            // A concurrent duplicate submit won the race; return the stored request.
            db.ChangeTracker.Clear();
            var id = await db.MaterialRequests.Where(m => m.WorkOrderId == workOrderId && m.ClientRequestId == req.ClientRequestId)
                .Select(m => m.Id).SingleAsync(ct);
            return new MaterialSubmitResult((await ProjectAsync([id], ct)).Single(), false);
        }
        return new MaterialSubmitResult((await ProjectAsync([mr.Id], ct)).Single(), true);
    }

    /// <summary>
    /// Approve or reject. The status change, the audit event and (for approvals) the outbox message are
    /// written in one SaveChanges call, i.e. one database transaction: either all of them commit or none do.
    /// Repeating the same decision is an idempotent no-op; a contradictory decision is a 409.
    /// </summary>
    public async Task<MaterialRequestDto> DecideAsync(Guid id, DecisionRequest req, CancellationToken ct)
    {
        var mr = await db.MaterialRequests.Include(m => m.WorkOrder).SingleOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NotFoundException("Material request not found.");
        var grant = await access.RequireManagerAsync(mr.WorkOrder!.ProjectId, ct);
        var decision = req.Decision!.Value;
        var target = decision == MaterialDecision.Approve ? MaterialRequestStatus.Approved : MaterialRequestStatus.Rejected;

        if (mr.Status != MaterialRequestStatus.Submitted)
        {
            if (mr.Status == target) return (await ProjectAsync([id], ct)).Single();
            throw new ConflictException("already-decided",
                $"This request was already {mr.Status.ToString().ToLowerInvariant()}; a decision cannot be reversed.");
        }
        if (req.Version is not null)
        {
            var expected = Versions.Decode(req.Version);
            if (!expected.AsSpan().SequenceEqual(mr.RowVersion))
            {
                throw new ConflictException("concurrency-conflict", "This request changed since you opened it. Reload and try again.");
            }
            db.Entry(mr).Property(m => m.RowVersion).OriginalValue = expected;
        }
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (target == MaterialRequestStatus.Rejected && note is null)
        {
            throw RequestValidationException.For(nameof(req.Note), "Give the crew a reason when rejecting a request.");
        }

        var now = clock.Now;
        mr.Status = target;
        mr.DecidedById = user.UserId;
        mr.DecidedAt = now;
        mr.DecisionNote = note;

        var code = await db.Projects.Where(p => p.Id == mr.WorkOrder.ProjectId).Select(p => p.Code).SingleAsync(ct);
        var woKey = WorkOrderService.Key(code, mr.WorkOrder.Number);
        audit.Record(grant.TenantId, mr.WorkOrder.ProjectId, "MaterialRequest", mr.Id,
            target == MaterialRequestStatus.Approved ? AuditActions.Approved : AuditActions.Rejected,
            $"{(target == MaterialRequestStatus.Approved ? "Approved" : "Rejected")} {mr.Quantity:0.##} {mr.Unit} {mr.ItemName} for {woKey}"
                + (note is null ? "" : $" — {note}"),
            new Dictionary<string, object?> { ["status"] = MaterialRequestStatus.Submitted },
            new Dictionary<string, object?> { ["status"] = target, ["note"] = note });

        if (target == MaterialRequestStatus.Approved)
        {
            var requester = await db.Users.Where(u => u.Id == mr.RequestedById).Select(u => u.DisplayName).SingleAsync(ct);
            var payload = new RequisitionPayload(
                RequisitionKey: OutboxKeys.ForApproval(mr.Id),
                TenantId: grant.TenantId,
                MaterialRequestId: mr.Id,
                ProjectCode: code,
                WorkOrderKey: woKey,
                ItemName: mr.ItemName,
                Quantity: mr.Quantity,
                Unit: mr.Unit,
                RequestedBy: requester,
                ApprovedBy: user.DisplayName,
                ApprovedAt: now);
            db.IntegrationOutbox.Add(new IntegrationOutbox
            {
                TenantId = grant.TenantId,
                EventType = ApprovedEventType,
                AggregateId = mr.Id,
                Payload = JsonSerializer.Serialize(payload, AuditWriter.JsonOptions),
                IdempotencyKey = payload.RequisitionKey,
                Status = OutboxStatus.Pending,
                NextRetryAt = now,
                CreatedAt = now,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (WorkOrderService.IsUniqueViolation(ex))
        {
            // Two approvals raced; the outbox idempotency key guarantees only one event exists.
            throw new ConflictException("already-decided", "This request was decided by someone else a moment ago.");
        }
        return (await ProjectAsync([id], ct)).Single();
    }

    private async Task<List<MaterialRequestDto>> ProjectAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var uid = user.UserId;
        var rows = await db.MaterialRequests.AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new
            {
                m.Id, ProjectId = m.WorkOrder!.ProjectId, ProjectCode = m.WorkOrder.Project!.Code, m.WorkOrderId,
                WoNumber = m.WorkOrder.Number, WoTitle = m.WorkOrder.Title, m.ItemName, m.Quantity, m.Unit, m.Justification, m.Status,
                m.RequestedById, RequestedByName = m.RequestedBy!.DisplayName, m.RequestedAt,
                m.DecidedById, DecidedByName = m.DecidedBy != null ? m.DecidedBy.DisplayName : null, m.DecidedAt, m.DecisionNote,
                m.ExternalReference, m.RowVersion,
                IsManager = m.WorkOrder.Project.Members.Any(x => x.UserId == uid && x.Role == ProjectRole.Manager),
            })
            .ToListAsync(ct);
        var outbox = await db.IntegrationOutbox.AsNoTracking()
            .Where(o => ids.Contains(o.AggregateId) && o.EventType == ApprovedEventType)
            .ToListAsync(ct);

        return ids.Select(id => rows.Single(r => r.Id == id)).Select(r =>
        {
            var o = outbox.FirstOrDefault(x => x.AggregateId == r.Id);
            return new MaterialRequestDto(
                r.Id, r.ProjectId, r.ProjectCode, r.WorkOrderId, WorkOrderService.Key(r.ProjectCode, r.WoNumber), r.WoTitle,
                r.ItemName, r.Quantity, r.Unit, r.Justification, r.Status,
                new UserRefDto(r.RequestedById, r.RequestedByName), r.RequestedAt,
                r.DecidedById is null ? null : new UserRefDto(r.DecidedById.Value, r.DecidedByName ?? ""),
                r.DecidedAt, r.DecisionNote, r.ExternalReference ?? o?.ExternalReference, Versions.Encode(r.RowVersion),
                r.IsManager && r.Status == MaterialRequestStatus.Submitted,
                ToErpStatus(r.Status, o));
        }).ToList();
    }

    public static ErpStatusDto ToErpStatus(MaterialRequestStatus status, IntegrationOutbox? o)
    {
        if (status != MaterialRequestStatus.Approved || o is null)
        {
            return new ErpStatusDto(ErpTransmissionStatus.NotApplicable, 0, null, null, null, null, null);
        }
        var s = o.Status switch
        {
            OutboxStatus.Succeeded => ErpTransmissionStatus.Sent,
            OutboxStatus.Failed => ErpTransmissionStatus.Failed,
            _ when o.Attempts > 0 => ErpTransmissionStatus.Retrying,
            _ => ErpTransmissionStatus.Queued,
        };
        return new ErpStatusDto(s, o.Attempts, o.Status is OutboxStatus.Pending or OutboxStatus.Processing ? o.NextRetryAt : null,
            o.LastError, o.ExternalReference, o.ProcessedAt, o.Id);
    }
}
