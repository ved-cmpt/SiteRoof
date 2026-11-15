using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Activity;
using SiteRoof.Api.Features.Projects;
using SiteRoof.Api.Features.WorkOrders;

namespace SiteRoof.Api.Features.Dashboard;

public sealed record StatusCountDto(WorkOrderStatus Status, int Count);

public sealed record WorkPackageDto(string WorkPackage, int Total, int Done, int Overdue, decimal PercentComplete);

public sealed record BlockerDto(
    Guid ProgressLogId,
    Guid WorkOrderId,
    string WorkOrderKey,
    string WorkOrderTitle,
    WorkOrderStatus WorkOrderStatus,
    string Blocker,
    string ReportedBy,
    DateTimeOffset ReportedAt,
    int AgeDays);

public sealed record PendingApprovalDto(Guid Id, Guid WorkOrderId, string WorkOrderKey, string ItemName, decimal Quantity, string Unit,
    string RequestedBy, DateTimeOffset RequestedAt, int AgeDays);

public sealed record ProjectKpisDto(int Total, int Done, int Open, int Overdue, int Blocked, int OpenBlockers, int PendingApprovals,
    int CriticalOpen, int PercentComplete, int DaysToTarget);

public sealed record ProjectDashboardDto(
    Guid ProjectId,
    string Code,
    string Title,
    ProjectStatus Status,
    DateOnly TargetDate,
    ProjectKpisDto Kpis,
    IReadOnlyList<StatusCountDto> StatusBreakdown,
    IReadOnlyList<WorkPackageDto> WorkPackages,
    IReadOnlyList<WorkOrderListItemDto> OverdueWork,
    IReadOnlyList<BlockerDto> OpenBlockers,
    IReadOnlyList<PendingApprovalDto> ApprovalBacklog,
    IReadOnlyList<MilestoneDto> Milestones,
    IReadOnlyList<ActivityItemDto> RecentActivity,
    DateTimeOffset GeneratedAt);

public sealed record ProjectStatusRowDto(Guid ProjectId, string Code, string Title, ProjectStatus Status, IReadOnlyList<StatusCountDto> Counts,
    int Overdue, int PercentComplete);

public sealed record AttentionItemDto(string Kind, string Severity, string Title, string Detail, string? ProjectCode, string LinkType, Guid LinkId,
    DateTimeOffset? Since);

public sealed record OverviewKpisDto(int ActiveProjects, int OpenWorkOrders, int OverdueWorkOrders, int BlockedWorkOrders, int OpenBlockers,
    int PendingApprovals, int ErpRetrying, int ErpFailed, int PercentComplete);

public sealed record OverviewDto(
    OverviewKpisDto Kpis,
    IReadOnlyList<ProjectStatusRowDto> Projects,
    IReadOnlyList<AttentionItemDto> Attention,
    IReadOnlyList<WorkOrderListItemDto> MyWork,
    DateTimeOffset GeneratedAt);

/// <summary>Dashboards are computed on every request from persisted rows; nothing is cached or hard-coded.</summary>
public sealed class DashboardService(
    SiteRoofDbContext db,
    ProjectAccess access,
    SiteClock clock,
    WorkOrderService workOrders,
    ProjectService projects,
    ActivityService activity)
{
    private static readonly WorkOrderStatus[] Active = [WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.Blocked];

    public async Task<ProjectDashboardDto> ForProjectAsync(Guid projectId, CancellationToken ct)
    {
        await access.ForProjectAsync(projectId, ct);
        var today = clock.Today;
        var now = clock.Now;
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);

        // T-SQL views: dbo.vw_ProjectWorkOrderRollup and dbo.vw_WorkPackageCompletion (see docs/DATABASE.md).
        var rollup = await db.ProjectWorkOrderRollups.AsNoTracking().SingleOrDefaultAsync(r => r.ProjectId == projectId, ct)
            ?? new ProjectWorkOrderRollup { ProjectId = projectId, TenantId = project.TenantId };
        var packages = await db.WorkPackageCompletions.AsNoTracking().Where(w => w.ProjectId == projectId)
            .OrderBy(w => w.WorkPackage).ToListAsync(ct);

        var blockers = await OpenBlockersAsync(projectId, project.Code, ct);
        var pending = await db.MaterialRequests.AsNoTracking()
            .Where(m => m.WorkOrder!.ProjectId == projectId && m.Status == MaterialRequestStatus.Submitted)
            .OrderBy(m => m.RequestedAt)
            .Select(m => new { m.Id, m.WorkOrderId, m.WorkOrder!.Number, m.ItemName, m.Quantity, m.Unit, By = m.RequestedBy!.DisplayName, m.RequestedAt })
            .ToListAsync(ct);

        var overdue = await workOrders.ListAsync(projectId, new WorkOrderQuery { Overdue = true, Sort = "due", PageSize = 10 }, ct);
        var detail = await projects.GetAsync(projectId, ct);
        var recent = await activity.ListAsync(new ActivityQuery { ProjectId = projectId, PageSize = 8 }, ct);

        var total = rollup.TotalWorkOrders - rollup.CancelledWorkOrders;
        var done = rollup.CompletedWorkOrders + rollup.ClosedWorkOrders;
        var kpis = new ProjectKpisDto(
            total, done, rollup.OpenWorkOrders + rollup.InProgressWorkOrders + rollup.BlockedWorkOrders,
            rollup.OverdueWorkOrders, rollup.BlockedWorkOrders, blockers.Count, pending.Count, rollup.CriticalOpenWorkOrders,
            ProjectService.Percent(done, total), project.TargetDate.DayNumber - today.DayNumber);

        return new ProjectDashboardDto(
            project.Id, project.Code, project.Title, project.Status, project.TargetDate, kpis,
            StatusCounts(rollup),
            packages.Select(p => new WorkPackageDto(p.WorkPackage, p.TotalWorkOrders, p.DoneWorkOrders, p.OverdueWorkOrders, p.PercentComplete)).ToList(),
            overdue.Items,
            blockers,
            pending.Select(m => new PendingApprovalDto(m.Id, m.WorkOrderId, WorkOrderService.Key(project.Code, m.Number), m.ItemName, m.Quantity, m.Unit,
                m.By, m.RequestedAt, AgeDays(now, m.RequestedAt))).ToList(),
            detail.Milestones,
            recent.Items,
            now);
    }

    public async Task<OverviewDto> OverviewAsync(CancellationToken ct)
    {
        var ids = access.AccessibleProjectIds();
        var now = clock.Now;
        var today = clock.Today;

        var rows = await (from p in db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id))
                          join r in db.ProjectWorkOrderRollups on p.Id equals r.ProjectId
                          orderby p.Status == ProjectStatus.Completed, p.TargetDate
                          select new { p.Id, p.Code, p.Title, p.Status, R = r }).ToListAsync(ct);

        var projectRows = rows.Select(x =>
        {
            var r = x.R;
            var total = r.TotalWorkOrders - r.CancelledWorkOrders;
            return new ProjectStatusRowDto(x.Id, x.Code, x.Title, x.Status, StatusCounts(r), r.OverdueWorkOrders,
                ProjectService.Percent(r.CompletedWorkOrders + r.ClosedWorkOrders, total));
        }).ToList();
        var codes = rows.ToDictionary(r => r.Id, r => r.Code);

        var openBlockers = await db.ProgressLogs.AsNoTracking()
            .Where(p => ids.Contains(p.WorkOrder!.ProjectId) && p.Blocker != null && p.BlockerResolvedAt == null
                && p.WorkOrder!.Status != WorkOrderStatus.Closed && p.WorkOrder!.Status != WorkOrderStatus.Cancelled)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Id, p.WorkOrderId, p.WorkOrder!.ProjectId, p.WorkOrder!.Number, p.WorkOrder!.Title, p.Blocker, p.CreatedAt })
            .ToListAsync(ct);
        var pending = await db.MaterialRequests.AsNoTracking()
            .Where(m => ids.Contains(m.WorkOrder!.ProjectId) && m.Status == MaterialRequestStatus.Submitted)
            .OrderBy(m => m.RequestedAt)
            .Select(m => new { m.Id, m.WorkOrderId, m.WorkOrder!.ProjectId, m.WorkOrder!.Number, m.ItemName, m.Quantity, m.Unit, m.RequestedAt })
            .ToListAsync(ct);
        var urgentOverdue = await db.WorkOrders.AsNoTracking()
            .Where(w => ids.Contains(w.ProjectId) && Active.Contains(w.Status) && w.DueDate < today)
            .OrderBy(w => w.DueDate)
            .Select(w => new { w.Id, w.ProjectId, w.Number, w.Title, w.DueDate, w.Priority, w.Status })
            .Take(25).ToListAsync(ct);
        var erp = await (from o in db.IntegrationOutbox.AsNoTracking()
                         join m in db.MaterialRequests.AsNoTracking() on o.AggregateId equals m.Id
                         where ids.Contains(m.WorkOrder!.ProjectId)
                               && (o.Status == OutboxStatus.Failed || (o.Status != OutboxStatus.Succeeded && o.Attempts > 0))
                         select new { o.Status, o.Attempts, o.LastError, o.NextRetryAt, m.WorkOrderId, m.WorkOrder!.ProjectId, m.ItemName, o.CreatedAt })
                        .ToListAsync(ct);
        var lateMilestones = await db.Milestones.AsNoTracking()
            .Where(m => ids.Contains(m.ProjectId) && m.Status != MilestoneStatus.Completed && m.DueDate < today)
            .Select(m => new { m.Id, m.ProjectId, m.Name, m.DueDate }).ToListAsync(ct);

        var attention = new List<AttentionItemDto>();
        attention.AddRange(erp.Select(e => new AttentionItemDto("erp", e.Status == OutboxStatus.Failed ? "critical" : "warning",
            e.Status == OutboxStatus.Failed ? $"ERP transmission failed: {e.ItemName}" : $"ERP retry scheduled: {e.ItemName}",
            e.Status == OutboxStatus.Failed ? $"{e.Attempts} attempts — {e.LastError}" : $"Attempt {e.Attempts} returned {e.LastError}",
            codes[e.ProjectId], "approvals", e.WorkOrderId, e.CreatedAt)));
        attention.AddRange(urgentOverdue.Select(w => new AttentionItemDto("overdue",
            w.Priority is WorkOrderPriority.Critical or WorkOrderPriority.High || w.Status == WorkOrderStatus.Blocked ? "critical" : "warning",
            $"{WorkOrderService.Key(codes[w.ProjectId], w.Number)} overdue: {w.Title}",
            $"Due {w.DueDate:MMM d} · {Text.Count(today.DayNumber - w.DueDate.DayNumber, "day")} late · {WorkOrderService.Label(w.Status)}",
            codes[w.ProjectId], "workOrder", w.Id, null)));
        attention.AddRange(openBlockers.Select(b => new AttentionItemDto("blocker", AgeDays(now, b.CreatedAt) >= 3 ? "critical" : "warning",
            $"Blocker on {WorkOrderService.Key(codes[b.ProjectId], b.Number)}: {b.Title}", Trim(b.Blocker!, 140),
            codes[b.ProjectId], "workOrder", b.WorkOrderId, b.CreatedAt)));
        attention.AddRange(pending.Select(m => new AttentionItemDto("approval", AgeDays(now, m.RequestedAt) >= 2 ? "warning" : "info",
            $"Approval needed: {m.Quantity:0.##} {m.Unit} {m.ItemName}",
            $"For {WorkOrderService.Key(codes[m.ProjectId], m.Number)} · waiting {Text.Count(AgeDays(now, m.RequestedAt), "day")}",
            codes[m.ProjectId], "approvals", m.Id, m.RequestedAt)));
        attention.AddRange(lateMilestones.Select(m => new AttentionItemDto("milestone", "warning",
            $"Milestone overdue: {m.Name}", $"Was due {m.DueDate:MMM d}", codes[m.ProjectId], "project", m.ProjectId, null)));

        var severityRank = new Dictionary<string, int> { ["critical"] = 0, ["warning"] = 1, ["info"] = 2 };
        var ordered = attention.OrderBy(a => severityRank[a.Severity]).ThenBy(a => a.Since ?? DateTimeOffset.MaxValue).Take(30).ToList();

        var totals = rows.Select(r => r.R).ToList();
        var allTotal = totals.Sum(r => r.TotalWorkOrders - r.CancelledWorkOrders);
        var allDone = totals.Sum(r => r.CompletedWorkOrders + r.ClosedWorkOrders);
        var kpis = new OverviewKpisDto(
            rows.Count(r => r.Status == ProjectStatus.Active),
            totals.Sum(r => r.OpenWorkOrders + r.InProgressWorkOrders + r.BlockedWorkOrders),
            totals.Sum(r => r.OverdueWorkOrders),
            totals.Sum(r => r.BlockedWorkOrders),
            openBlockers.Count,
            pending.Count,
            erp.Count(e => e.Status != OutboxStatus.Failed),
            erp.Count(e => e.Status == OutboxStatus.Failed),
            ProjectService.Percent(allDone, allTotal));

        var mine = await workOrders.ListMineAsync(ct);
        return new OverviewDto(kpis, projectRows, ordered, mine, now);
    }

    internal async Task<List<BlockerDto>> OpenBlockersAsync(Guid projectId, string code, CancellationToken ct)
    {
        var now = clock.Now;
        var rows = await db.ProgressLogs.AsNoTracking()
            .Where(p => p.WorkOrder!.ProjectId == projectId && p.Blocker != null && p.BlockerResolvedAt == null
                && p.WorkOrder!.Status != WorkOrderStatus.Closed && p.WorkOrder!.Status != WorkOrderStatus.Cancelled)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new { p.Id, p.WorkOrderId, p.WorkOrder!.Number, p.WorkOrder!.Title, p.WorkOrder!.Status, p.Blocker, By = p.Reporter!.DisplayName, p.CreatedAt })
            .ToListAsync(ct);
        return rows.Select(r => new BlockerDto(r.Id, r.WorkOrderId, WorkOrderService.Key(code, r.Number), r.Title, r.Status, r.Blocker!, r.By,
            r.CreatedAt, AgeDays(now, r.CreatedAt))).ToList();
    }

    private static List<StatusCountDto> StatusCounts(ProjectWorkOrderRollup r) =>
    [
        new(WorkOrderStatus.Open, r.OpenWorkOrders),
        new(WorkOrderStatus.InProgress, r.InProgressWorkOrders),
        new(WorkOrderStatus.Blocked, r.BlockedWorkOrders),
        new(WorkOrderStatus.Completed, r.CompletedWorkOrders),
        new(WorkOrderStatus.Closed, r.ClosedWorkOrders),
    ];

    internal static int AgeDays(DateTimeOffset now, DateTimeOffset since) => Math.Max(0, (int)(now - since).TotalDays);

    private static string Trim(string s, int n) => s.Length > n ? s[..n] + "…" : s;
}

[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class DashboardController(DashboardService dashboards) : ControllerBase
{
    /// <summary>Tenant-wide KPIs, per-project status counts and the attention-required queue.</summary>
    [HttpGet("overview")]
    public Task<OverviewDto> Overview(CancellationToken ct) => dashboards.OverviewAsync(ct);

    /// <summary>Project dashboard: overdue work, completion by work package, open blockers, approval backlog, recent activity.</summary>
    [HttpGet("projects/{id:guid}/dashboard")]
    public Task<ProjectDashboardDto> Project(Guid id, CancellationToken ct) => dashboards.ForProjectAsync(id, ct);
}
