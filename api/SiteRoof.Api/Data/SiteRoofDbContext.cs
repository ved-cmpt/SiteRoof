using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Data;

public sealed class SiteRoofDbContext(DbContextOptions<SiteRoofDbContext> options, ITenantContext tenantContext)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    /// <summary>
    /// Tenant used by global query filters. Null (no signed-in user) matches nothing, so a
    /// forgotten authorization check fails closed. System code uses IgnoreQueryFilters().
    /// </summary>
    private Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMembership> ProjectMemberships => Set<ProjectMembership>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderComment> WorkOrderComments => Set<WorkOrderComment>();
    public DbSet<WorkOrderDependency> WorkOrderDependencies => Set<WorkOrderDependency>();
    public DbSet<ProgressLog> ProgressLogs => Set<ProgressLog>();
    public DbSet<MaterialRequest> MaterialRequests => Set<MaterialRequest>();
    public DbSet<IntegrationOutbox> IntegrationOutbox => Set<IntegrationOutbox>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<MockErpRequisition> MockErpRequisitions => Set<MockErpRequisition>();
    public DbSet<MockErpCall> MockErpCalls => Set<MockErpCall>();

    // Keyless types mapped to T-SQL views (see Migrations/*_DashboardViews and docs/DATABASE.md).
    public DbSet<ProjectWorkOrderRollup> ProjectWorkOrderRollups => Set<ProjectWorkOrderRollup>();
    public DbSet<WorkPackageCompletion> WorkPackageCompletions => Set<WorkPackageCompletion>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(12, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.ErpSimulatorMode).HasConversion<string>().HasMaxLength(24);
            e.HasIndex(x => new { x.IsDemo, x.ExpiresAt });
        });

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.JobTitle).HasMaxLength(120);
            e.Property(x => x.PersonaKey).HasMaxLength(24);
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.PersonaKey });
        });

        b.Entity<Project>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ClientName).HasMaxLength(160);
            e.Property(x => x.SiteAddress).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(80);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Projects_Dates", "[TargetDate] >= [StartDate]");
                t.HasCheckConstraint("CK_Projects_Status", "[Status] IN ('Planning','Active','OnHold','Completed')");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<ProjectMembership>(e =>
        {
            e.HasKey(x => new { x.ProjectId, x.UserId });
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Trade).HasMaxLength(60);
            e.HasOne(x => x.Project).WithMany(p => p.Members).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.UserId);
            e.ToTable(t => t.HasCheckConstraint("CK_ProjectMemberships_Role", "[Role] IN ('Manager','Supervisor','Crew')"));
            e.HasQueryFilter(x => x.Project!.TenantId == CurrentTenantId);
        });

        b.Entity<Milestone>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Project).WithMany(p => p.Milestones).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProjectId, x.DueDate });
            e.ToTable(t => t.HasCheckConstraint("CK_Milestones_Status", "[Status] IN ('Planned','InProgress','Completed')"));
            e.HasQueryFilter(x => x.Project!.TenantId == CurrentTenantId);
        });

        b.Entity<WorkOrder>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(160);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.WorkPackage).HasMaxLength(60);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Project).WithMany(p => p.WorkOrders).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Milestone).WithMany().HasForeignKey(x => x.MilestoneId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            // Board and dashboard queries filter by project + status and sort/compare by due date.
            e.HasIndex(x => new { x.ProjectId, x.Status, x.DueDate }).IncludeProperties(x => new { x.WorkPackage, x.Priority });
            e.HasIndex(x => new { x.AssigneeId, x.Status });
            e.HasIndex(x => x.TenantId);
            e.Ignore(x => x.IsLocked);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_WorkOrders_PlannedQuantity", "[PlannedQuantity] >= 0");
                t.HasCheckConstraint("CK_WorkOrders_Number", "[Number] > 0");
                t.HasCheckConstraint("CK_WorkOrders_Status", "[Status] IN ('Open','InProgress','Blocked','Completed','Closed','Cancelled')");
                t.HasCheckConstraint("CK_WorkOrders_Priority", "[Priority] IN ('Low','Normal','High','Critical')");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<WorkOrderComment>(e =>
        {
            e.Property(x => x.Body).HasMaxLength(2000);
            e.HasOne(x => x.WorkOrder).WithMany(w => w.Comments).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WorkOrderId, x.CreatedAt });
            e.HasQueryFilter(x => x.WorkOrder!.TenantId == CurrentTenantId);
        });

        b.Entity<WorkOrderDependency>(e =>
        {
            e.HasKey(x => new { x.WorkOrderId, x.DependsOnWorkOrderId });
            e.HasOne(x => x.WorkOrder).WithMany(w => w.Dependencies).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DependsOn).WithMany().HasForeignKey(x => x.DependsOnWorkOrderId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.DependsOnWorkOrderId);
            e.ToTable(t => t.HasCheckConstraint("CK_WorkOrderDependencies_NotSelf", "[WorkOrderId] <> [DependsOnWorkOrderId]"));
            e.HasQueryFilter(x => x.WorkOrder!.TenantId == CurrentTenantId);
        });

        b.Entity<ProgressLog>(e =>
        {
            e.Property(x => x.Blocker).HasMaxLength(1000);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasOne(x => x.WorkOrder).WithMany(w => w.ProgressLogs).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.VerifiedBy).WithMany().HasForeignKey(x => x.VerifiedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.BlockerResolvedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WorkOrderId, x.CreatedAt });
            e.Ignore(x => x.HasOpenBlocker);
            e.ToTable(t => t.HasCheckConstraint("CK_ProgressLogs_Quantity", "[Quantity] >= 0"));
            e.HasQueryFilter(x => x.WorkOrder!.TenantId == CurrentTenantId);
        });

        b.Entity<MaterialRequest>(e =>
        {
            e.Property(x => x.ItemName).HasMaxLength(160);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Justification).HasMaxLength(1000);
            e.Property(x => x.DecisionNote).HasMaxLength(1000);
            e.Property(x => x.ExternalReference).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.WorkOrder).WithMany(w => w.MaterialRequests).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WorkOrderId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.RequestedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_MaterialRequests_Quantity", "[Quantity] > 0");
                t.HasCheckConstraint("CK_MaterialRequests_Status", "[Status] IN ('Submitted','Approved','Rejected')");
                t.HasCheckConstraint("CK_MaterialRequests_Decision",
                    "([Status] = 'Submitted' AND [DecidedById] IS NULL) OR ([Status] <> 'Submitted' AND [DecidedById] IS NOT NULL AND [DecidedAt] IS NOT NULL)");
            });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<IntegrationOutbox>(e =>
        {
            e.ToTable("IntegrationOutbox", t =>
            {
                t.HasCheckConstraint("CK_IntegrationOutbox_Attempts", "[Attempts] >= 0");
                t.HasCheckConstraint("CK_IntegrationOutbox_Status", "[Status] IN ('Pending','Processing','Succeeded','Failed')");
            });
            e.Property(x => x.EventType).HasMaxLength(80);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.ExternalReference).HasMaxLength(64);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            // Dispatcher polling: due pending messages.
            e.HasIndex(x => new { x.Status, x.NextRetryAt });
            e.HasIndex(x => new { x.TenantId, x.AggregateId });
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.Property(x => x.Action).HasMaxLength(40);
            e.Property(x => x.Summary).HasMaxLength(400);
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        b.Entity<MockErpRequisition>(e =>
        {
            e.ToTable("MockErpRequisitions", "mockerp");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.ExternalReference).HasMaxLength(64);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.TenantId);
        });

        b.Entity<MockErpCall>(e =>
        {
            e.ToTable("MockErpCalls", "mockerp");
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.HasIndex(x => new { x.IdempotencyKey, x.ReceivedAt });
            e.HasIndex(x => x.TenantId);
        });

        ConfigureViews(b);
    }

    private static void ConfigureViews(ModelBuilder b)
    {
        b.Entity<ProjectWorkOrderRollup>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_ProjectWorkOrderRollup");
        });
        b.Entity<WorkPackageCompletion>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_WorkPackageCompletion");
        });
    }
}

/// <summary>Row of dbo.vw_ProjectWorkOrderRollup.</summary>
public sealed class ProjectWorkOrderRollup
{
    public Guid ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public int TotalWorkOrders { get; set; }
    public int OpenWorkOrders { get; set; }
    public int InProgressWorkOrders { get; set; }
    public int BlockedWorkOrders { get; set; }
    public int CompletedWorkOrders { get; set; }
    public int ClosedWorkOrders { get; set; }
    public int CancelledWorkOrders { get; set; }
    public int OverdueWorkOrders { get; set; }
    public int CriticalOpenWorkOrders { get; set; }
}

/// <summary>Row of dbo.vw_WorkPackageCompletion.</summary>
public sealed class WorkPackageCompletion
{
    public Guid ProjectId { get; set; }
    public required string WorkPackage { get; set; }
    public int TotalWorkOrders { get; set; }
    public int DoneWorkOrders { get; set; }
    public int OverdueWorkOrders { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ReportedQuantity { get; set; }

    /// <summary>Average per-work-order completion (done = 100, otherwise reported/planned capped at 100).</summary>
    public decimal PercentComplete { get; set; }
}
