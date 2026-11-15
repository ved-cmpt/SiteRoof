using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Projects;

public sealed class ProjectService(
    SiteRoofDbContext db,
    ICurrentUser user,
    ProjectAccess access,
    AuditWriter audit,
    SiteClock clock)
{
    private static readonly WorkOrderStatus[] DoneStatuses = [WorkOrderStatus.Completed, WorkOrderStatus.Closed];

    public async Task<IReadOnlyList<ProjectSummaryDto>> ListAsync(ProjectStatus? status, string? search, CancellationToken ct)
    {
        var ids = access.AccessibleProjectIds();
        var uid = user.UserId;
        var query = db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id));
        if (status is not null) query = query.Where(p => p.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(p => p.Title.Contains(s) || p.Code.Contains(s) || (p.City != null && p.City.Contains(s)));
        }

        var rows = await (
            from p in query
            // Every project has exactly one row in the view (it LEFT JOINs work orders), so an inner join is safe.
            join r in db.ProjectWorkOrderRollups on p.Id equals r.ProjectId
            orderby p.Status == ProjectStatus.Completed, p.TargetDate
            select new
            {
                p.Id, p.Code, p.Title, p.Status, p.City, p.StartDate, p.TargetDate,
                MyRole = p.Members.Where(m => m.UserId == uid).Select(m => (ProjectRole?)m.Role).FirstOrDefault(),
                Total = r.TotalWorkOrders - r.CancelledWorkOrders,
                Done = r.CompletedWorkOrders + r.ClosedWorkOrders,
                Overdue = r.OverdueWorkOrders,
                Blocked = r.BlockedWorkOrders,
            }).ToListAsync(ct);

        return rows.Select(x => new ProjectSummaryDto(
            x.Id, x.Code, x.Title, x.Status, x.City, x.StartDate, x.TargetDate, x.MyRole,
            x.Total, x.Done, x.Overdue, x.Blocked, Percent(x.Done, x.Total))).ToList();
    }

    public async Task<ProjectDetailDto> GetAsync(Guid projectId, CancellationToken ct)
    {
        var grant = await access.ForProjectAsync(projectId, ct);
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId, ct);
        var today = clock.Today;

        var members = await db.ProjectMemberships.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Role).ThenBy(m => m.User!.DisplayName)
            .Select(m => new MemberDto(
                m.UserId, m.User!.DisplayName, m.User.JobTitle, m.Role, m.Trade,
                db.WorkOrders.Count(w => w.ProjectId == projectId && w.AssigneeId == m.UserId
                    && w.Status != WorkOrderStatus.Closed && w.Status != WorkOrderStatus.Cancelled && w.Status != WorkOrderStatus.Completed)))
            .ToListAsync(ct);

        var milestones = await db.Milestones.AsNoTracking()
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.DueDate)
            .Select(m => new MilestoneDto(
                m.Id, m.Name, m.DueDate, m.Status,
                m.Status != MilestoneStatus.Completed && m.DueDate < today,
                db.WorkOrders.Count(w => w.MilestoneId == m.Id && w.Status != WorkOrderStatus.Cancelled),
                db.WorkOrders.Count(w => w.MilestoneId == m.Id && DoneStatuses.Contains(w.Status)),
                m.CompletedAt))
            .ToListAsync(ct);

        return new ProjectDetailDto(
            project.Id, project.Code, project.Title, project.Description, project.ClientName, project.SiteAddress,
            project.City, project.Status, project.StartDate, project.TargetDate, grant.MembershipRole,
            new ProjectPermissionsDto(grant.CanManage, grant.CanSupervise, grant.IsMember),
            members, milestones);
    }

    public async Task<ProjectDetailDto> CreateAsync(CreateProjectRequest req, CancellationToken ct)
    {
        if (!user.IsInRole(TenantRoles.ProjectManager) && !user.IsInRole(TenantRoles.Admin))
        {
            throw new ForbiddenException("Only project managers and admins can create projects.");
        }
        var tenantId = user.RequireTenantId();
        if (req.TargetDate < req.StartDate)
        {
            throw RequestValidationException.For(nameof(req.TargetDate), "Target date must be on or after the start date.");
        }
        var code = req.Code.Trim().ToUpperInvariant();
        if (await db.Projects.AnyAsync(p => p.Code == code, ct))
        {
            throw new ConflictException("duplicate-code", $"A project with code {code} already exists in this workspace.");
        }
        if (await DemoQuotas.IsDemoTenantAsync(db, tenantId, ct) && await db.Projects.CountAsync(ct) >= DemoQuotas.MaxProjects)
        {
            throw new BusinessRuleException("quota-exceeded", $"Demo workspaces are limited to {DemoQuotas.MaxProjects} projects.");
        }

        var project = new Project
        {
            TenantId = tenantId,
            Code = code,
            Title = req.Title.Trim(),
            Description = req.Description?.Trim(),
            ClientName = req.ClientName?.Trim(),
            SiteAddress = req.SiteAddress?.Trim(),
            City = req.City?.Trim(),
            Status = ProjectStatus.Planning,
            StartDate = req.StartDate!.Value,
            TargetDate = req.TargetDate!.Value,
            CreatedAt = clock.Now,
        };
        project.Members.Add(new ProjectMembership { UserId = user.UserId, Role = ProjectRole.Manager, AddedAt = clock.Now });
        db.Projects.Add(project);
        audit.Record(tenantId, project.Id, "Project", project.Id, AuditActions.Created,
            $"Created project {project.Code} · {project.Title}", after: Snapshot(project));
        await db.SaveChangesAsync(ct);
        return await GetAsync(project.Id, ct);
    }

    public async Task<ProjectDetailDto> UpdateAsync(Guid projectId, UpdateProjectRequest req, CancellationToken ct)
    {
        var grant = await access.RequireManagerAsync(projectId, ct);
        var project = await db.Projects.SingleAsync(p => p.Id == projectId, ct);
        var before = Snapshot(project);

        if (req.Title is not null) project.Title = req.Title.Trim();
        if (req.Description is not null) project.Description = req.Description.Trim();
        if (req.ClientName is not null) project.ClientName = req.ClientName.Trim();
        if (req.SiteAddress is not null) project.SiteAddress = req.SiteAddress.Trim();
        if (req.City is not null) project.City = req.City.Trim();
        if (req.Status is not null) project.Status = req.Status.Value;
        if (req.StartDate is not null) project.StartDate = req.StartDate.Value;
        if (req.TargetDate is not null) project.TargetDate = req.TargetDate.Value;
        if (project.TargetDate < project.StartDate)
        {
            throw RequestValidationException.For(nameof(req.TargetDate), "Target date must be on or after the start date.");
        }

        var after = Snapshot(project);
        if (AuditWriter.Diff(before, after).After is { Count: > 0 })
        {
            audit.Record(grant.TenantId, projectId, "Project", projectId, AuditActions.Updated,
                $"Updated project {project.Code}", before, after);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(projectId, ct);
    }

    public async Task<IReadOnlyList<MilestoneDto>> ListMilestonesAsync(Guid projectId, CancellationToken ct) =>
        (await GetAsync(projectId, ct)).Milestones;

    public async Task<MilestoneDto> CreateMilestoneAsync(Guid projectId, CreateMilestoneRequest req, CancellationToken ct)
    {
        var grant = await access.RequireManagerAsync(projectId, ct);
        var sort = await db.Milestones.Where(m => m.ProjectId == projectId).Select(m => (int?)m.SortOrder).MaxAsync(ct) ?? 0;
        var milestone = new Milestone { ProjectId = projectId, Name = req.Name.Trim(), DueDate = req.DueDate!.Value, SortOrder = sort + 1 };
        db.Milestones.Add(milestone);
        audit.Record(grant.TenantId, projectId, "Milestone", milestone.Id, AuditActions.Created,
            $"Added milestone \"{milestone.Name}\" due {milestone.DueDate:yyyy-MM-dd}",
            after: new Dictionary<string, object?> { ["name"] = milestone.Name, ["dueDate"] = milestone.DueDate, ["status"] = milestone.Status });
        await db.SaveChangesAsync(ct);
        return (await ListMilestonesAsync(projectId, ct)).Single(m => m.Id == milestone.Id);
    }

    public async Task<MilestoneDto> UpdateMilestoneAsync(Guid milestoneId, UpdateMilestoneRequest req, CancellationToken ct)
    {
        var milestone = await db.Milestones.SingleOrDefaultAsync(m => m.Id == milestoneId, ct) ?? throw new NotFoundException("Milestone not found.");
        var grant = await access.RequireManagerAsync(milestone.ProjectId, ct);
        var before = new Dictionary<string, object?> { ["name"] = milestone.Name, ["dueDate"] = milestone.DueDate, ["status"] = milestone.Status };
        if (req.Name is not null) milestone.Name = req.Name.Trim();
        if (req.DueDate is not null) milestone.DueDate = req.DueDate.Value;
        if (req.Status is not null && req.Status != milestone.Status)
        {
            milestone.Status = req.Status.Value;
            milestone.CompletedAt = milestone.Status == MilestoneStatus.Completed ? clock.Now : null;
        }
        var after = new Dictionary<string, object?> { ["name"] = milestone.Name, ["dueDate"] = milestone.DueDate, ["status"] = milestone.Status };
        audit.Record(grant.TenantId, milestone.ProjectId, "Milestone", milestone.Id,
            req.Status is not null ? AuditActions.StatusChanged : AuditActions.Updated,
            $"Updated milestone \"{milestone.Name}\"", before, after);
        await db.SaveChangesAsync(ct);
        return (await ListMilestonesAsync(milestone.ProjectId, ct)).Single(m => m.Id == milestone.Id);
    }

    public async Task<IReadOnlyList<TenantUserDto>> ListTenantUsersAsync(CancellationToken ct)
    {
        var tenantId = user.RequireTenantId();
        var users = await db.Users.AsNoTracking().Where(u => u.TenantId == tenantId).OrderBy(u => u.DisplayName).ToListAsync(ct);
        var roleRows = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              join u in db.Users on ur.UserId equals u.Id
                              where u.TenantId == tenantId
                              select new { ur.UserId, r.Name }).ToListAsync(ct);
        return users.Select(u => new TenantUserDto(u.Id, u.DisplayName, u.JobTitle,
            roleRows.Where(r => r.UserId == u.Id).Select(r => r.Name!).ToList())).ToList();
    }

    public async Task<ProjectDetailDto> AddMemberAsync(Guid projectId, AddMemberRequest req, CancellationToken ct)
    {
        var grant = await access.RequireManagerAsync(projectId, ct);
        var target = await db.Users.SingleOrDefaultAsync(u => u.Id == req.UserId && u.TenantId == grant.TenantId, ct)
            ?? throw RequestValidationException.For(nameof(req.UserId), "That user is not part of this workspace.");
        if (await db.ProjectMemberships.AnyAsync(m => m.ProjectId == projectId && m.UserId == target.Id, ct))
        {
            throw new ConflictException("already-member", $"{target.DisplayName} is already on this project.");
        }
        db.ProjectMemberships.Add(new ProjectMembership
        {
            ProjectId = projectId, UserId = target.Id, Role = req.Role!.Value, Trade = req.Trade?.Trim(), AddedAt = clock.Now,
        });
        audit.Record(grant.TenantId, projectId, "Project", projectId, AuditActions.Updated,
            $"Added {target.DisplayName} to the roster as {req.Role}",
            after: new Dictionary<string, object?> { ["member"] = target.DisplayName, ["role"] = req.Role, ["trade"] = req.Trade });
        await db.SaveChangesAsync(ct);
        return await GetAsync(projectId, ct);
    }

    internal static int Percent(int done, int total) => total <= 0 ? 0 : (int)Math.Round(100.0 * done / total);

    private static Dictionary<string, object?> Snapshot(Project p) => new()
    {
        ["code"] = p.Code,
        ["title"] = p.Title,
        ["description"] = p.Description,
        ["clientName"] = p.ClientName,
        ["siteAddress"] = p.SiteAddress,
        ["city"] = p.City,
        ["status"] = p.Status,
        ["startDate"] = p.StartDate,
        ["targetDate"] = p.TargetDate,
    };
}
