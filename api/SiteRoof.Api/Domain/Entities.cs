using Microsoft.AspNetCore.Identity;

namespace SiteRoof.Api.Domain;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsDemo { get; set; }
    public ErpSimulatorMode ErpSimulatorMode { get; set; } = ErpSimulatorMode.Normal;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class AppUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public required string DisplayName { get; set; }
    public string? JobTitle { get; set; }

    /// <summary>Demo persona key (manager, supervisor, crew) or null for non-persona users.</summary>
    public string? PersonaKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Project
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Code { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? ClientName { get; set; }
    public string? SiteAddress { get; set; }
    public string? City { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;
    public DateOnly StartDate { get; set; }
    public DateOnly TargetDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<ProjectMembership> Members { get; set; } = [];
    public List<Milestone> Milestones { get; set; } = [];
    public List<WorkOrder> WorkOrders { get; set; } = [];
}

public sealed class ProjectMembership
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public ProjectRole Role { get; set; }
    public string? Trade { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

public sealed class Milestone
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public required string Name { get; set; }
    public DateOnly DueDate { get; set; }
    public MilestoneStatus Status { get; set; } = MilestoneStatus.Planned;
    public DateTimeOffset? CompletedAt { get; set; }
    public int SortOrder { get; set; }
}

public sealed class WorkOrder
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string WorkPackage { get; set; }
    public Guid? AssigneeId { get; set; }
    public AppUser? Assignee { get; set; }
    public Guid? MilestoneId { get; set; }
    public Milestone? Milestone { get; set; }
    public DateOnly DueDate { get; set; }
    public WorkOrderPriority Priority { get; set; } = WorkOrderPriority.Normal;
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;
    public decimal PlannedQuantity { get; set; }
    public string? Unit { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>SQL Server rowversion used for optimistic concurrency.</summary>
    public byte[] RowVersion { get; set; } = [];

    public List<ProgressLog> ProgressLogs { get; set; } = [];
    public List<WorkOrderComment> Comments { get; set; } = [];
    public List<WorkOrderDependency> Dependencies { get; set; } = [];
    public List<MaterialRequest> MaterialRequests { get; set; } = [];

    public bool IsLocked => Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled;
}

public sealed class WorkOrderComment
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public Guid AuthorId { get; set; }
    public AppUser? Author { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class WorkOrderDependency
{
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public Guid DependsOnWorkOrderId { get; set; }
    public WorkOrder? DependsOn { get; set; }
}

public sealed class ProgressLog
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public Guid ReporterId { get; set; }
    public AppUser? Reporter { get; set; }
    public decimal Quantity { get; set; }
    public string? Blocker { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? VerifiedById { get; set; }
    public AppUser? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public Guid? BlockerResolvedById { get; set; }
    public DateTimeOffset? BlockerResolvedAt { get; set; }

    public bool HasOpenBlocker => !string.IsNullOrWhiteSpace(Blocker) && BlockerResolvedAt is null;
}

public sealed class MaterialRequest
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }
    public required string ItemName { get; set; }
    public decimal Quantity { get; set; }
    public required string Unit { get; set; }
    public string? Justification { get; set; }
    public MaterialRequestStatus Status { get; set; } = MaterialRequestStatus.Submitted;
    public Guid RequestedById { get; set; }
    public AppUser? RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? DecidedById { get; set; }
    public AppUser? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Client-generated key that makes submission idempotent (unique per work order).</summary>
    public Guid ClientRequestId { get; set; }

    public string? ExternalReference { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class IntegrationOutbox
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string EventType { get; set; }
    public Guid AggregateId { get; set; }
    public required string Payload { get; set; }
    public required string IdempotencyKey { get; set; }
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextRetryAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public string? ExternalReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ActorId { get; set; }
    public AppUser? Actor { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Action { get; set; }
    public required string Summary { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Requisition stored by the self-hosted mock ERP (one per idempotency key).</summary>
public sealed class MockErpRequisition
{
    public Guid Id { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string ExternalReference { get; set; }
    public Guid TenantId { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Every call received by the mock ERP, including rejected ones (for diagnostics and tests).</summary>
public sealed class MockErpCall
{
    public long Id { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid TenantId { get; set; }
    public int ResponseStatus { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
