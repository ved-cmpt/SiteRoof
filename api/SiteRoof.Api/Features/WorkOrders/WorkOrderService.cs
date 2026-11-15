using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.WorkOrders;

public sealed class WorkOrderService(
    SiteRoofDbContext db,
    ICurrentUser user,
    ProjectAccess access,
    AuditWriter audit,
    SiteClock clock)
{
    private static readonly WorkOrderStatus[] ActiveStatuses = [WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.Blocked];

    // ---------------------------------------------------------------- queries

    public async Task<PagedResult<WorkOrderListItemDto>> ListAsync(Guid projectId, WorkOrderQuery q, CancellationToken ct)
    {
        await access.ForProjectAsync(projectId, ct);
        var today = clock.Today;
        var uid = user.UserId;

        var query = db.WorkOrders.AsNoTracking().Where(w => w.ProjectId == projectId);
        if (q.Status is not null) query = query.Where(w => w.Status == q.Status);
        if (q.AssigneeId is not null) query = query.Where(w => w.AssigneeId == q.AssigneeId);
        if (q.Mine == true) query = query.Where(w => w.AssigneeId == uid);
        if (q.Priority is not null) query = query.Where(w => w.Priority == q.Priority);
        if (!string.IsNullOrWhiteSpace(q.WorkPackage)) query = query.Where(w => w.WorkPackage == q.WorkPackage);
        if (q.Overdue == true) query = query.Where(w => w.DueDate < today && ActiveStatuses.Contains(w.Status));
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = int.TryParse(s.Split('-').Last(), out var n)
                ? query.Where(w => w.Title.Contains(s) || w.Number == n)
                : query.Where(w => w.Title.Contains(s) || w.WorkPackage.Contains(s));
        }

        query = q.Sort switch
        {
            "due" => query.OrderBy(w => w.DueDate).ThenBy(w => w.Number),
            "priority" => query.OrderByDescending(w => w.Priority == WorkOrderPriority.Critical)
                .ThenByDescending(w => w.Priority == WorkOrderPriority.High).ThenBy(w => w.DueDate),
            _ => query.OrderBy(w => w.Number),
        };

        var code = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Code).SingleAsync(ct);
        var page = new PageQuery(q.Page, q.PageSize);
        var total = await query.CountAsync(ct);
        var rows = await Project(query, today).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<WorkOrderListItemDto>(rows.Select(r => r.ToDto(code)).ToList(), page.SafePage, page.SafePageSize, total);
    }

    /// <summary>Work assigned to the caller across every accessible project (crew "My work" view).</summary>
    public async Task<IReadOnlyList<WorkOrderListItemDto>> ListMineAsync(CancellationToken ct)
    {
        var ids = access.AccessibleProjectIds();
        var uid = user.UserId;
        var today = clock.Today;
        var query = db.WorkOrders.AsNoTracking()
            .Where(w => ids.Contains(w.ProjectId) && w.AssigneeId == uid && ActiveStatuses.Contains(w.Status))
            .OrderBy(w => w.DueDate);
        var codes = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var rows = await Project(query, today).Take(100).ToListAsync(ct);
        return rows.Select(r => r.ToDto(codes[r.ProjectId])).ToList();
    }

    private IQueryable<ListRow> Project(IQueryable<WorkOrder> query, DateOnly today) =>
        query.Select(w => new ListRow
        {
            Id = w.Id,
            ProjectId = w.ProjectId,
            Number = w.Number,
            Title = w.Title,
            WorkPackage = w.WorkPackage,
            Status = w.Status,
            Priority = w.Priority,
            DueDate = w.DueDate,
            IsOverdue = w.DueDate < today && ActiveStatuses.Contains(w.Status),
            AssigneeId = w.AssigneeId,
            AssigneeName = w.Assignee != null ? w.Assignee.DisplayName : null,
            PlannedQuantity = w.PlannedQuantity,
            ReportedQuantity = w.ProgressLogs.Sum(p => (decimal?)p.Quantity) ?? 0,
            Unit = w.Unit,
            OpenBlockers = w.ProgressLogs.Count(p => p.Blocker != null && p.BlockerResolvedAt == null),
            PendingMaterialRequests = w.MaterialRequests.Count(m => m.Status == MaterialRequestStatus.Submitted),
        });

    public async Task<WorkOrderDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: false, ct);
        var today = clock.Today;
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == wo.ProjectId).Select(p => new { p.Code, p.Title }).SingleAsync(ct);

        var assignee = wo.AssigneeId is null ? null
            : await db.Users.Where(u => u.Id == wo.AssigneeId).Select(u => new UserRefDto(u.Id, u.DisplayName)).SingleAsync(ct);
        var createdBy = await db.Users.Where(u => u.Id == wo.CreatedById).Select(u => new UserRefDto(u.Id, u.DisplayName)).SingleAsync(ct);
        var milestone = wo.MilestoneId is null ? null
            : await db.Milestones.Where(m => m.Id == wo.MilestoneId).Select(m => new MilestoneRefDto(m.Id, m.Name, m.DueDate)).SingleOrDefaultAsync(ct);

        var dependencies = await db.WorkOrderDependencies.AsNoTracking()
            .Where(d => d.WorkOrderId == id)
            .Select(d => new { d.DependsOn!.Id, d.DependsOn.Number, d.DependsOn.Title, d.DependsOn.Status })
            .ToListAsync(ct);
        var dependents = await db.WorkOrderDependencies.AsNoTracking()
            .Where(d => d.DependsOnWorkOrderId == id)
            .Select(d => new { d.WorkOrder!.Id, d.WorkOrder.Number, d.WorkOrder.Title, d.WorkOrder.Status })
            .ToListAsync(ct);

        var progress = await db.ProgressLogs.AsNoTracking()
            .Where(p => p.WorkOrderId == id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProgressLogDto(
                p.Id, new UserRefDto(p.ReporterId, p.Reporter!.DisplayName), p.Quantity, p.Blocker, p.Note, p.CreatedAt,
                p.VerifiedById == null ? null : new UserRefDto(p.VerifiedById.Value, p.VerifiedBy!.DisplayName),
                p.VerifiedAt, p.BlockerResolvedAt))
            .ToListAsync(ct);

        var comments = await db.WorkOrderComments.AsNoTracking()
            .Where(c => c.WorkOrderId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(c.Id, new UserRefDto(c.AuthorId, c.Author!.DisplayName), c.Body, c.CreatedAt))
            .ToListAsync(ct);

        var actor = ActorFor(wo, grant);
        var reported = progress.Sum(p => p.Quantity);
        var key = Key(project.Code, wo.Number);
        var active = WorkOrderStateMachine.IsActive(wo.Status);
        var permissions = new WorkOrderPermissionsDto(
            CanEdit: grant.CanSupervise && !wo.IsLocked,
            CanAssign: grant.CanSupervise && !wo.IsLocked,
            CanReportProgress: (actor.IsAssignee || grant.CanSupervise) && active,
            CanVerify: grant.CanSupervise,
            CanComment: grant.IsMember && !wo.IsLocked,
            CanManageDependencies: grant.CanSupervise && !wo.IsLocked,
            CanRequestMaterials: (actor.IsAssignee || grant.CanSupervise) && active,
            CanDecideMaterials: grant.CanManage);

        return new WorkOrderDetailDto(
            wo.Id, wo.ProjectId, project.Code, project.Title, key, wo.Number, wo.Title, wo.Description, wo.WorkPackage,
            wo.Status, wo.Priority, wo.DueDate, wo.DueDate < today && active, wo.IsLocked,
            assignee, milestone, wo.PlannedQuantity, reported, wo.Unit, PercentOf(reported, wo.PlannedQuantity, wo.Status),
            createdBy, wo.CreatedAt, wo.UpdatedAt, wo.CompletedAt, wo.ClosedAt, Versions.Encode(wo.RowVersion),
            grant.IsMember ? WorkOrderStateMachine.Available(wo.Status, actor) : [],
            permissions,
            dependencies.Select(d => new WorkOrderRefDto(d.Id, Key(project.Code, d.Number), d.Title, d.Status)).ToList(),
            dependents.Select(d => new WorkOrderRefDto(d.Id, Key(project.Code, d.Number), d.Title, d.Status)).ToList(),
            progress, comments);
    }

    // ---------------------------------------------------------------- commands

    public async Task<WorkOrderDetailDto> CreateAsync(Guid projectId, CreateWorkOrderRequest req, CancellationToken ct)
    {
        var grant = await access.RequireSupervisorAsync(projectId, ct);
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        if (project.Status == ProjectStatus.Completed)
        {
            throw new BusinessRuleException("project-completed", "This project is completed; new work orders cannot be added.");
        }
        if (req.AssigneeId is not null) await EnsureMemberAsync(projectId, req.AssigneeId.Value, nameof(req.AssigneeId), ct);
        if (req.MilestoneId is not null) await EnsureMilestoneAsync(projectId, req.MilestoneId.Value, ct);
        if (await DemoQuotas.IsDemoTenantAsync(db, grant.TenantId, ct)
            && await db.WorkOrders.CountAsync(w => w.ProjectId == projectId, ct) >= DemoQuotas.MaxWorkOrdersPerProject)
        {
            throw new BusinessRuleException("quota-exceeded", $"Demo projects are limited to {DemoQuotas.MaxWorkOrdersPerProject} work orders.");
        }

        var dependsOn = (req.DependsOn ?? []).Distinct().ToList();
        if (dependsOn.Count > 0)
        {
            var valid = await db.WorkOrders.CountAsync(w => w.ProjectId == projectId && dependsOn.Contains(w.Id), ct);
            if (valid != dependsOn.Count)
            {
                throw RequestValidationException.For(nameof(req.DependsOn), "Dependencies must be work orders in the same project.");
            }
        }

        for (var attempt = 1; ; attempt++)
        {
            var next = (await db.WorkOrders.Where(w => w.ProjectId == projectId).MaxAsync(w => (int?)w.Number, ct) ?? 0) + 1;
            var now = clock.Now;
            var wo = new WorkOrder
            {
                TenantId = grant.TenantId,
                ProjectId = projectId,
                Number = next,
                Title = req.Title.Trim(),
                Description = req.Description?.Trim(),
                WorkPackage = req.WorkPackage.Trim(),
                AssigneeId = req.AssigneeId,
                MilestoneId = req.MilestoneId,
                DueDate = req.DueDate!.Value,
                Priority = req.Priority,
                PlannedQuantity = req.PlannedQuantity,
                Unit = req.Unit?.Trim(),
                Status = WorkOrderStatus.Open,
                CreatedById = user.UserId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var dep in dependsOn) wo.Dependencies.Add(new WorkOrderDependency { DependsOnWorkOrderId = dep });
            db.WorkOrders.Add(wo);

            var key = Key(project.Code, next);
            audit.Record(grant.TenantId, projectId, "WorkOrder", wo.Id, AuditActions.Created,
                $"Created {key} · {wo.Title}", after: Snapshot(wo));
            if (wo.AssigneeId is not null)
            {
                var name = await db.Users.Where(u => u.Id == wo.AssigneeId).Select(u => u.DisplayName).SingleAsync(ct);
                audit.Record(grant.TenantId, projectId, "WorkOrder", wo.Id, AuditActions.Assigned,
                    $"Assigned {key} to {name}", after: new Dictionary<string, object?> { ["assignee"] = name });
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return await GetAsync(wo.Id, ct);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex) && attempt < 3)
            {
                // Another request took the same number; retry with the next one.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<WorkOrderDetailDto> UpdateAsync(Guid id, UpdateWorkOrderRequest req, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: true, ct);
        if (!grant.CanSupervise) throw new ForbiddenException("Only a project manager or site supervisor can edit work orders.");
        EnsureNotLocked(wo, await CodeAsync(wo.ProjectId, ct));
        EnsureVersion(wo, req.Version);

        var before = Snapshot(wo);
        var beforeAssignee = wo.AssigneeId;
        if (req.Title is not null) wo.Title = req.Title.Trim();
        if (req.Description is not null) wo.Description = req.Description.Trim();
        if (req.WorkPackage is not null) wo.WorkPackage = req.WorkPackage.Trim();
        if (req.DueDate is not null) wo.DueDate = req.DueDate.Value;
        if (req.Priority is not null) wo.Priority = req.Priority.Value;
        if (req.PlannedQuantity is not null) wo.PlannedQuantity = req.PlannedQuantity.Value;
        if (req.Unit is not null) wo.Unit = req.Unit.Trim();
        if (req.MilestoneId is not null)
        {
            await EnsureMilestoneAsync(wo.ProjectId, req.MilestoneId.Value, ct);
            wo.MilestoneId = req.MilestoneId;
        }
        if (req.Unassign)
        {
            if (wo.Status != WorkOrderStatus.Open)
            {
                throw new BusinessRuleException("assignee-required", "Work that has started must keep an assignee; reassign it instead.");
            }
            wo.AssigneeId = null;
        }
        else if (req.AssigneeId is not null)
        {
            await EnsureMemberAsync(wo.ProjectId, req.AssigneeId.Value, nameof(req.AssigneeId), ct);
            wo.AssigneeId = req.AssigneeId;
        }

        var after = Snapshot(wo);
        if (AuditWriter.Diff(before, after).After is not { Count: > 0 }) return await GetAsync(id, ct);

        var key = Key(await CodeAsync(wo.ProjectId, ct), wo.Number);
        wo.UpdatedAt = clock.Now;
        if (beforeAssignee != wo.AssigneeId)
        {
            var name = wo.AssigneeId is null ? "nobody"
                : await db.Users.Where(u => u.Id == wo.AssigneeId).Select(u => u.DisplayName).SingleAsync(ct);
            audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.Assigned, $"Assigned {key} to {name}",
                new Dictionary<string, object?> { ["assigneeId"] = beforeAssignee },
                new Dictionary<string, object?> { ["assigneeId"] = wo.AssigneeId });
        }
        audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.Updated, $"Updated {key} · {wo.Title}", before, after);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<WorkOrderDetailDto> TransitionAsync(Guid id, TransitionRequest req, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: true, ct);
        if (!grant.IsMember) throw new ForbiddenException("Only project members can change work-order status.");
        var transition = req.Transition!.Value;
        var code = await CodeAsync(wo.ProjectId, ct);
        var key = Key(code, wo.Number);
        EnsureVersion(wo, req.Version);

        var target = WorkOrderStateMachine.Target(wo.Status, transition);
        if (target is null)
        {
            if (wo.IsLocked) EnsureNotLocked(wo, code);
            throw new ConflictException("invalid-transition", $"{key} is {Label(wo.Status)}; it cannot be {Past(transition)}.");
        }
        if (!WorkOrderStateMachine.IsPermitted(wo.Status, transition, ActorFor(wo, grant)))
        {
            throw new ForbiddenException(transition == WorkOrderTransition.Reopen && wo.Status == WorkOrderStatus.Closed
                ? "Only the project manager can reopen closed work."
                : "You can only change the status of work assigned to you.");
        }

        var reason = req.Reason?.Trim();
        switch (transition)
        {
            case WorkOrderTransition.Start when wo.AssigneeId is null:
                throw new BusinessRuleException("assignee-required", $"Assign {key} to a crew member before starting it.");
            case WorkOrderTransition.Complete:
                var open = await db.WorkOrderDependencies
                    .Where(d => d.WorkOrderId == id && d.DependsOn!.Status != WorkOrderStatus.Completed && d.DependsOn.Status != WorkOrderStatus.Closed)
                    .Select(d => d.DependsOn!.Number).ToListAsync(ct);
                if (open.Count > 0)
                {
                    throw new BusinessRuleException("dependencies-incomplete",
                        $"{key} depends on {string.Join(", ", open.Order().Select(n => Key(code, n)))}, which must be completed first.");
                }
                break;
            case WorkOrderTransition.Close:
                var unverified = await db.ProgressLogs.CountAsync(p => p.WorkOrderId == id && p.VerifiedAt == null, ct);
                var blockers = await db.ProgressLogs.CountAsync(p => p.WorkOrderId == id && p.Blocker != null && p.BlockerResolvedAt == null, ct);
                var pending = await db.MaterialRequests.CountAsync(m => m.WorkOrderId == id && m.Status == MaterialRequestStatus.Submitted, ct);
                var problems = new List<string>();
                if (unverified > 0) problems.Add($"{unverified} unverified progress log(s)");
                if (blockers > 0) problems.Add($"{blockers} open blocker(s)");
                if (pending > 0) problems.Add($"{pending} pending material request(s)");
                if (problems.Count > 0)
                {
                    throw new BusinessRuleException("close-preconditions", $"{key} cannot be closed yet: {string.Join(", ", problems)}.");
                }
                break;
            case WorkOrderTransition.Reopen when wo.Status == WorkOrderStatus.Closed && string.IsNullOrWhiteSpace(reason):
            case WorkOrderTransition.Cancel when string.IsNullOrWhiteSpace(reason):
                throw RequestValidationException.For(nameof(req.Reason), "A reason is required for this change.");
        }

        var from = wo.Status;
        wo.Status = target.Value;
        wo.UpdatedAt = clock.Now;
        if (target == WorkOrderStatus.Completed) wo.CompletedAt = clock.Now;
        if (target == WorkOrderStatus.Closed) wo.ClosedAt = clock.Now;
        if (target == WorkOrderStatus.InProgress && from is WorkOrderStatus.Completed or WorkOrderStatus.Closed)
        {
            wo.CompletedAt = null;
            wo.ClosedAt = null;
        }

        var after = new Dictionary<string, object?> { ["status"] = wo.Status };
        if (reason is not null) after["reason"] = reason;
        audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.StatusChanged,
            $"Moved {key} from {Label(from)} to {Label(wo.Status)}" + (reason is null ? "" : $" — {reason}"),
            new Dictionary<string, object?> { ["status"] = from }, after);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<CommentDto> AddCommentAsync(Guid id, CreateCommentRequest req, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: false, ct);
        if (!grant.IsMember) throw new ForbiddenException("Only project members can comment.");
        var code = await CodeAsync(wo.ProjectId, ct);
        EnsureNotLocked(wo, code);
        if (await DemoQuotas.IsDemoTenantAsync(db, grant.TenantId, ct)
            && await db.WorkOrderComments.CountAsync(c => c.WorkOrderId == id, ct) >= DemoQuotas.MaxCommentsPerWorkOrder)
        {
            throw new BusinessRuleException("quota-exceeded", "This demo work order has reached its comment limit.");
        }
        var comment = new WorkOrderComment { WorkOrderId = id, AuthorId = user.UserId, Body = req.Body.Trim(), CreatedAt = clock.Now };
        db.WorkOrderComments.Add(comment);
        var excerpt = comment.Body.Length > 120 ? comment.Body[..120] + "…" : comment.Body;
        audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.Commented,
            $"Commented on {Key(code, wo.Number)}: \"{excerpt}\"", after: new Dictionary<string, object?> { ["comment"] = excerpt });
        await db.SaveChangesAsync(ct);
        return new CommentDto(comment.Id, new UserRefDto(user.UserId, user.DisplayName), comment.Body, comment.CreatedAt);
    }

    public async Task<IReadOnlyList<CommentDto>> ListCommentsAsync(Guid id, CancellationToken ct) => (await GetAsync(id, ct)).Comments;

    public async Task<WorkOrderDetailDto> AddDependencyAsync(Guid id, AddDependencyRequest req, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: false, ct);
        if (!grant.CanSupervise) throw new ForbiddenException("Only a project manager or site supervisor can change dependencies.");
        var code = await CodeAsync(wo.ProjectId, ct);
        EnsureNotLocked(wo, code);
        var depId = req.DependsOnWorkOrderId!.Value;
        if (depId == id) throw RequestValidationException.For(nameof(req.DependsOnWorkOrderId), "A work order cannot depend on itself.");
        var dep = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(w => w.Id == depId && w.ProjectId == wo.ProjectId, ct)
            ?? throw RequestValidationException.For(nameof(req.DependsOnWorkOrderId), "Dependencies must be work orders in the same project.");
        if (await db.WorkOrderDependencies.AnyAsync(d => d.WorkOrderId == id && d.DependsOnWorkOrderId == depId, ct))
        {
            throw new ConflictException("duplicate-dependency", "That dependency already exists.");
        }
        if (await CreatesCycleAsync(wo.ProjectId, id, depId, ct))
        {
            throw new BusinessRuleException("dependency-cycle", $"{Key(code, dep.Number)} already depends on {Key(code, wo.Number)}; this would create a cycle.");
        }
        db.WorkOrderDependencies.Add(new WorkOrderDependency { WorkOrderId = id, DependsOnWorkOrderId = depId });
        audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.DependencyAdded,
            $"{Key(code, wo.Number)} now depends on {Key(code, dep.Number)}",
            after: new Dictionary<string, object?> { ["dependsOn"] = Key(code, dep.Number) });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<WorkOrderDetailDto> RemoveDependencyAsync(Guid id, Guid dependsOnId, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: false, ct);
        if (!grant.CanSupervise) throw new ForbiddenException("Only a project manager or site supervisor can change dependencies.");
        var code = await CodeAsync(wo.ProjectId, ct);
        EnsureNotLocked(wo, code);
        var link = await db.WorkOrderDependencies.Include(d => d.DependsOn)
            .SingleOrDefaultAsync(d => d.WorkOrderId == id && d.DependsOnWorkOrderId == dependsOnId, ct)
            ?? throw new NotFoundException("Dependency not found.");
        db.WorkOrderDependencies.Remove(link);
        audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.DependencyRemoved,
            $"{Key(code, wo.Number)} no longer depends on {Key(code, link.DependsOn!.Number)}",
            before: new Dictionary<string, object?> { ["dependsOn"] = Key(code, link.DependsOn.Number) });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---------------------------------------------------------------- progress

    public async Task<IReadOnlyList<ProgressLogDto>> ListProgressAsync(Guid id, CancellationToken ct) => (await GetAsync(id, ct)).Progress;

    public async Task<WorkOrderDetailDto> AddProgressAsync(Guid id, CreateProgressRequest req, CancellationToken ct)
    {
        var (wo, grant) = await LoadAsync(id, tracking: true, ct);
        var actor = ActorFor(wo, grant);
        if (!actor.IsAssignee && !grant.CanSupervise)
        {
            throw new ForbiddenException("Only the assigned crew member or a supervisor can report progress on this work order.");
        }
        var code = await CodeAsync(wo.ProjectId, ct);
        var key = Key(code, wo.Number);
        EnsureNotLocked(wo, code);
        if (wo.Status == WorkOrderStatus.Completed)
        {
            throw new BusinessRuleException("work-order-completed", $"{key} is completed. Reopen it to report more progress.");
        }
        var blocker = string.IsNullOrWhiteSpace(req.Blocker) ? null : req.Blocker.Trim();
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (req.Quantity == 0 && blocker is null && note is null)
        {
            throw RequestValidationException.For(nameof(req.Quantity), "Report a quantity, a blocker or a note.");
        }
        if (await DemoQuotas.IsDemoTenantAsync(db, grant.TenantId, ct)
            && await db.ProgressLogs.CountAsync(p => p.WorkOrderId == id, ct) >= DemoQuotas.MaxProgressLogsPerWorkOrder)
        {
            throw new BusinessRuleException("quota-exceeded", "This demo work order has reached its progress-log limit.");
        }

        var log = new ProgressLog
        {
            WorkOrderId = id, ReporterId = user.UserId, Quantity = req.Quantity, Blocker = blocker, Note = note, CreatedAt = clock.Now,
        };
        db.ProgressLogs.Add(log);
        var unit = wo.Unit is null ? "" : $" {wo.Unit}";
        audit.Record(grant.TenantId, wo.ProjectId, "ProgressLog", log.Id, AuditActions.ProgressReported,
            $"Reported {req.Quantity:0.##}{unit} on {key}" + (blocker is null ? "" : $" with blocker: {Excerpt(blocker)}"),
            after: new Dictionary<string, object?> { ["workOrder"] = key, ["quantity"] = req.Quantity, ["blocker"] = blocker, ["note"] = note });

        // Field reality drives status: first progress starts the work, a reported blocker blocks it.
        var from = wo.Status;
        if (wo.Status == WorkOrderStatus.Open && wo.AssigneeId is not null) wo.Status = WorkOrderStatus.InProgress;
        if (blocker is not null && wo.Status == WorkOrderStatus.InProgress) wo.Status = WorkOrderStatus.Blocked;
        if (from != wo.Status)
        {
            audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.StatusChanged,
                $"Moved {key} from {Label(from)} to {Label(wo.Status)} (from progress report)",
                new Dictionary<string, object?> { ["status"] = from }, new Dictionary<string, object?> { ["status"] = wo.Status });
        }
        wo.UpdatedAt = clock.Now;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<WorkOrderDetailDto> VerifyProgressAsync(Guid progressId, CancellationToken ct)
    {
        var log = await db.ProgressLogs.SingleOrDefaultAsync(p => p.Id == progressId, ct) ?? throw new NotFoundException("Progress log not found.");
        var (wo, grant) = await LoadAsync(log.WorkOrderId, tracking: false, ct);
        if (!grant.CanSupervise) throw new ForbiddenException("Only a site supervisor or project manager can verify progress.");
        if (log.VerifiedAt is not null) throw new ConflictException("already-verified", "This progress log has already been verified.");
        if (log.ReporterId == user.UserId)
        {
            throw new BusinessRuleException("self-verification", "Progress must be verified by someone other than the reporter.");
        }
        log.VerifiedById = user.UserId;
        log.VerifiedAt = clock.Now;
        var key = Key(await CodeAsync(wo.ProjectId, ct), wo.Number);
        audit.Record(grant.TenantId, wo.ProjectId, "ProgressLog", log.Id, AuditActions.ProgressVerified,
            $"Verified {log.Quantity:0.##} {wo.Unit} reported on {key}",
            new Dictionary<string, object?> { ["verified"] = false }, new Dictionary<string, object?> { ["verified"] = true });
        await db.SaveChangesAsync(ct);
        return await GetAsync(wo.Id, ct);
    }

    public async Task<WorkOrderDetailDto> ResolveBlockerAsync(Guid progressId, CancellationToken ct)
    {
        var log = await db.ProgressLogs.SingleOrDefaultAsync(p => p.Id == progressId, ct) ?? throw new NotFoundException("Progress log not found.");
        var (wo, grant) = await LoadAsync(log.WorkOrderId, tracking: true, ct);
        if (!grant.CanSupervise) throw new ForbiddenException("Only a site supervisor or project manager can resolve blockers.");
        if (log.Blocker is null) throw new BusinessRuleException("no-blocker", "This progress log has no blocker.");
        if (log.BlockerResolvedAt is not null) throw new ConflictException("already-resolved", "This blocker has already been resolved.");
        log.BlockerResolvedAt = clock.Now;
        log.BlockerResolvedById = user.UserId;
        var key = Key(await CodeAsync(wo.ProjectId, ct), wo.Number);
        audit.Record(grant.TenantId, wo.ProjectId, "ProgressLog", log.Id, AuditActions.BlockerResolved,
            $"Resolved blocker on {key}: {Excerpt(log.Blocker)}",
            new Dictionary<string, object?> { ["blocker"] = "open" }, new Dictionary<string, object?> { ["blocker"] = "resolved" });

        var othersOpen = await db.ProgressLogs.AnyAsync(p => p.WorkOrderId == wo.Id && p.Id != log.Id && p.Blocker != null && p.BlockerResolvedAt == null, ct);
        if (wo.Status == WorkOrderStatus.Blocked && !othersOpen)
        {
            wo.Status = WorkOrderStatus.InProgress;
            wo.UpdatedAt = clock.Now;
            audit.Record(grant.TenantId, wo.ProjectId, "WorkOrder", wo.Id, AuditActions.StatusChanged,
                $"Moved {key} from Blocked to In progress (all blockers resolved)",
                new Dictionary<string, object?> { ["status"] = WorkOrderStatus.Blocked }, new Dictionary<string, object?> { ["status"] = wo.Status });
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(wo.Id, ct);
    }

    // ---------------------------------------------------------------- helpers

    internal async Task<(WorkOrder Wo, ProjectAccessGrant Grant)> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = tracking ? db.WorkOrders : db.WorkOrders.AsNoTracking();
        var wo = await q.SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Work order not found.");
        var grant = await access.ForProjectAsync(wo.ProjectId, ct);
        return (wo, grant);
    }

    private WorkOrderActor ActorFor(WorkOrder wo, ProjectAccessGrant grant) =>
        new(grant.IsMember && wo.AssigneeId == user.UserId, grant.CanSupervise, grant.CanManage);

    private void EnsureVersion(WorkOrder wo, string? version)
    {
        var expected = Versions.Decode(version);
        if (!expected.AsSpan().SequenceEqual(wo.RowVersion))
        {
            var ex = new ConflictException("concurrency-conflict",
                "This work order was changed by someone else since you opened it. Reload to see the latest version, then try again.");
            ex.Extensions["currentVersion"] = Versions.Encode(wo.RowVersion);
            ex.Extensions["currentStatus"] = wo.Status.ToString();
            throw ex;
        }
        // Also guard the UPDATE itself (WHERE RowVersion = @expected) against a race after this check.
        db.Entry(wo).Property(w => w.RowVersion).OriginalValue = expected;
    }

    private static void EnsureNotLocked(WorkOrder wo, string code)
    {
        if (wo.IsLocked)
        {
            throw new ConflictException("work-order-locked",
                $"{Key(code, wo.Number)} is {Label(wo.Status).ToLowerInvariant()} and locked. " +
                (wo.Status == WorkOrderStatus.Closed ? "A project manager must reopen it before it can change." : "Cancelled work cannot change."));
        }
    }

    private async Task EnsureMemberAsync(Guid projectId, Guid userId, string field, CancellationToken ct)
    {
        if (!await db.ProjectMemberships.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId, ct))
        {
            throw RequestValidationException.For(field, "Work can only be assigned to members of this project's team.");
        }
    }

    private async Task EnsureMilestoneAsync(Guid projectId, Guid milestoneId, CancellationToken ct)
    {
        if (!await db.Milestones.AnyAsync(m => m.Id == milestoneId && m.ProjectId == projectId, ct))
        {
            throw RequestValidationException.For("milestoneId", "The milestone must belong to this project.");
        }
    }

    private async Task<bool> CreatesCycleAsync(Guid projectId, Guid workOrderId, Guid dependsOnId, CancellationToken ct)
    {
        // Adding workOrder -> dependsOn creates a cycle if dependsOn already (transitively) depends on workOrder.
        var edges = await db.WorkOrderDependencies.AsNoTracking()
            .Where(d => d.WorkOrder!.ProjectId == projectId)
            .Select(d => new { d.WorkOrderId, d.DependsOnWorkOrderId })
            .ToListAsync(ct);
        var graph = edges.ToLookup(e => e.WorkOrderId, e => e.DependsOnWorkOrderId);
        var stack = new Stack<Guid>([dependsOnId]);
        var seen = new HashSet<Guid>();
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node == workOrderId) return true;
            if (!seen.Add(node)) continue;
            foreach (var next in graph[node]) stack.Push(next);
        }
        return false;
    }

    private Task<string> CodeAsync(Guid projectId, CancellationToken ct) =>
        db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Code).SingleAsync(ct);

    public static string Key(string projectCode, int number) => $"{projectCode}-{number:000}";

    internal static int PercentOf(decimal reported, decimal planned, WorkOrderStatus status)
    {
        if (WorkOrderStateMachine.IsDone(status)) return 100;
        if (planned <= 0) return 0;
        return (int)Math.Min(100, Math.Round(100m * reported / planned));
    }

    public static string Label(WorkOrderStatus s) => s switch
    {
        WorkOrderStatus.InProgress => "In progress",
        _ => s.ToString(),
    };

    private static string Past(WorkOrderTransition t) => t switch
    {
        WorkOrderTransition.Start => "started",
        WorkOrderTransition.Block => "blocked",
        WorkOrderTransition.Unblock => "unblocked",
        WorkOrderTransition.Complete => "completed",
        WorkOrderTransition.Reopen => "reopened",
        WorkOrderTransition.Close => "closed",
        WorkOrderTransition.Cancel => "cancelled",
        _ => t.ToString().ToLowerInvariant(),
    };

    private static string Excerpt(string s) => s.Length > 140 ? s[..140] + "…" : s;

    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };

    private static Dictionary<string, object?> Snapshot(WorkOrder w) => new()
    {
        ["title"] = w.Title,
        ["description"] = w.Description,
        ["workPackage"] = w.WorkPackage,
        ["assigneeId"] = w.AssigneeId,
        ["milestoneId"] = w.MilestoneId,
        ["dueDate"] = w.DueDate,
        ["priority"] = w.Priority,
        ["plannedQuantity"] = w.PlannedQuantity,
        ["unit"] = w.Unit,
        ["status"] = w.Status,
    };

    private sealed class ListRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public int Number { get; init; }
        public string Title { get; init; } = "";
        public string WorkPackage { get; init; } = "";
        public WorkOrderStatus Status { get; init; }
        public WorkOrderPriority Priority { get; init; }
        public DateOnly DueDate { get; init; }
        public bool IsOverdue { get; init; }
        public Guid? AssigneeId { get; init; }
        public string? AssigneeName { get; init; }
        public decimal PlannedQuantity { get; init; }
        public decimal ReportedQuantity { get; init; }
        public string? Unit { get; init; }
        public int OpenBlockers { get; init; }
        public int PendingMaterialRequests { get; init; }

        public WorkOrderListItemDto ToDto(string code) => new(
            Id, ProjectId, Key(code, Number), Number, Title, WorkPackage, Status, Priority, DueDate, IsOverdue,
            AssigneeId is null ? null : new UserRefDto(AssigneeId.Value, AssigneeName ?? ""),
            PlannedQuantity, ReportedQuantity, Unit, PercentOf(ReportedQuantity, PlannedQuantity, Status), OpenBlockers, PendingMaterialRequests);
    }
}
