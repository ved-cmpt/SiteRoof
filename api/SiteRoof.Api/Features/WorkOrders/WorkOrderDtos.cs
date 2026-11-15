using System.ComponentModel.DataAnnotations;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.WorkOrders;

public sealed record UserRefDto(Guid Id, string DisplayName);

public sealed record WorkOrderListItemDto(
    Guid Id,
    Guid ProjectId,
    string Key,
    int Number,
    string Title,
    string WorkPackage,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    DateOnly DueDate,
    bool IsOverdue,
    UserRefDto? Assignee,
    decimal PlannedQuantity,
    decimal ReportedQuantity,
    string? Unit,
    int PercentComplete,
    int OpenBlockers,
    int PendingMaterialRequests);

public sealed record WorkOrderRefDto(Guid Id, string Key, string Title, WorkOrderStatus Status);

public sealed record ProgressLogDto(
    Guid Id,
    UserRefDto Reporter,
    decimal Quantity,
    string? Blocker,
    string? Note,
    DateTimeOffset CreatedAt,
    UserRefDto? VerifiedBy,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset? BlockerResolvedAt);

public sealed record CommentDto(Guid Id, UserRefDto Author, string Body, DateTimeOffset CreatedAt);

public sealed record WorkOrderPermissionsDto(
    bool CanEdit,
    bool CanAssign,
    bool CanReportProgress,
    bool CanVerify,
    bool CanComment,
    bool CanManageDependencies,
    bool CanRequestMaterials,
    bool CanDecideMaterials);

public sealed record MilestoneRefDto(Guid Id, string Name, DateOnly DueDate);

public sealed record WorkOrderDetailDto(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string ProjectTitle,
    string Key,
    int Number,
    string Title,
    string? Description,
    string WorkPackage,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    DateOnly DueDate,
    bool IsOverdue,
    bool IsLocked,
    UserRefDto? Assignee,
    MilestoneRefDto? Milestone,
    decimal PlannedQuantity,
    decimal ReportedQuantity,
    string? Unit,
    int PercentComplete,
    UserRefDto CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ClosedAt,
    string Version,
    IReadOnlyList<WorkOrderTransition> AvailableTransitions,
    WorkOrderPermissionsDto Permissions,
    IReadOnlyList<WorkOrderRefDto> Dependencies,
    IReadOnlyList<WorkOrderRefDto> Dependents,
    IReadOnlyList<ProgressLogDto> Progress,
    IReadOnlyList<CommentDto> Comments);

public sealed class WorkOrderQuery
{
    public WorkOrderStatus? Status { get; set; }
    public Guid? AssigneeId { get; set; }
    public string? WorkPackage { get; set; }
    public WorkOrderPriority? Priority { get; set; }
    public bool? Overdue { get; set; }
    public bool? Mine { get; set; }
    [StringLength(100)] public string? Search { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class CreateWorkOrderRequest
{
    [Required, StringLength(160, MinimumLength = 3)] public string Title { get; set; } = "";
    [StringLength(4000)] public string? Description { get; set; }
    [Required, StringLength(60, MinimumLength = 2)] public string WorkPackage { get; set; } = "";
    public Guid? AssigneeId { get; set; }
    public Guid? MilestoneId { get; set; }
    [Required] public DateOnly? DueDate { get; set; }
    public WorkOrderPriority Priority { get; set; } = WorkOrderPriority.Normal;
    [Range(0, 1_000_000)] public decimal PlannedQuantity { get; set; }
    [StringLength(16)] public string? Unit { get; set; }
    public List<Guid>? DependsOn { get; set; }
}

/// <summary>Partial update. Omitted (null) fields are unchanged; <see cref="Unassign"/> clears the assignee.</summary>
public sealed class UpdateWorkOrderRequest
{
    [Required] public string? Version { get; set; }
    [StringLength(160, MinimumLength = 3)] public string? Title { get; set; }
    [StringLength(4000)] public string? Description { get; set; }
    [StringLength(60, MinimumLength = 2)] public string? WorkPackage { get; set; }
    public Guid? AssigneeId { get; set; }
    public bool Unassign { get; set; }
    public Guid? MilestoneId { get; set; }
    public DateOnly? DueDate { get; set; }
    public WorkOrderPriority? Priority { get; set; }
    [Range(0, 1_000_000)] public decimal? PlannedQuantity { get; set; }
    [StringLength(16)] public string? Unit { get; set; }
}

public sealed class TransitionRequest
{
    [Required] public WorkOrderTransition? Transition { get; set; }
    [Required] public string? Version { get; set; }
    [StringLength(500)] public string? Reason { get; set; }
}

public sealed class CreateCommentRequest
{
    [Required, StringLength(2000, MinimumLength = 1)] public string Body { get; set; } = "";
}

public sealed class AddDependencyRequest
{
    [Required] public Guid? DependsOnWorkOrderId { get; set; }
}

public sealed class CreateProgressRequest
{
    [Range(0, 100_000, ErrorMessage = "Quantity must be between 0 and 100,000.")] public decimal Quantity { get; set; }
    [StringLength(1000)] public string? Blocker { get; set; }
    [StringLength(2000)] public string? Note { get; set; }
}
