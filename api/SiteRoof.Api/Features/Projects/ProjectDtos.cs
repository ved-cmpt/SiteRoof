using System.ComponentModel.DataAnnotations;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Projects;

public sealed record ProjectSummaryDto(
    Guid Id,
    string Code,
    string Title,
    ProjectStatus Status,
    string? City,
    DateOnly StartDate,
    DateOnly TargetDate,
    ProjectRole? MyRole,
    int TotalWorkOrders,
    int DoneWorkOrders,
    int OverdueWorkOrders,
    int BlockedWorkOrders,
    int PercentComplete);

public sealed record MemberDto(Guid UserId, string DisplayName, string? JobTitle, ProjectRole Role, string? Trade, int OpenAssignments);

public sealed record MilestoneDto(
    Guid Id,
    string Name,
    DateOnly DueDate,
    MilestoneStatus Status,
    bool IsOverdue,
    int WorkOrderCount,
    int DoneWorkOrderCount,
    DateTimeOffset? CompletedAt);

public sealed record ProjectPermissionsDto(bool CanManage, bool CanSupervise, bool IsMember);

public sealed record ProjectDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    string? ClientName,
    string? SiteAddress,
    string? City,
    ProjectStatus Status,
    DateOnly StartDate,
    DateOnly TargetDate,
    ProjectRole? MyRole,
    ProjectPermissionsDto Permissions,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<MilestoneDto> Milestones);

public sealed class CreateProjectRequest
{
    [Required, StringLength(20, MinimumLength = 2), RegularExpression("^[A-Z0-9-]+$", ErrorMessage = "Use uppercase letters, digits and dashes.")]
    public string Code { get; set; } = "";

    [Required, StringLength(160, MinimumLength = 3)]
    public string Title { get; set; } = "";

    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(160)] public string? ClientName { get; set; }
    [StringLength(200)] public string? SiteAddress { get; set; }
    [StringLength(80)] public string? City { get; set; }
    [Required] public DateOnly? StartDate { get; set; }
    [Required] public DateOnly? TargetDate { get; set; }
}

public sealed class UpdateProjectRequest
{
    [StringLength(160, MinimumLength = 3)] public string? Title { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
    [StringLength(160)] public string? ClientName { get; set; }
    [StringLength(200)] public string? SiteAddress { get; set; }
    [StringLength(80)] public string? City { get; set; }
    public ProjectStatus? Status { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? TargetDate { get; set; }
}

public sealed class CreateMilestoneRequest
{
    [Required, StringLength(160, MinimumLength = 3)] public string Name { get; set; } = "";
    [Required] public DateOnly? DueDate { get; set; }
}

public sealed class UpdateMilestoneRequest
{
    [StringLength(160, MinimumLength = 3)] public string? Name { get; set; }
    public DateOnly? DueDate { get; set; }
    public MilestoneStatus? Status { get; set; }
}

public sealed class AddMemberRequest
{
    [Required] public Guid? UserId { get; set; }
    [Required] public ProjectRole? Role { get; set; }
    [StringLength(60)] public string? Trade { get; set; }
}

public sealed record TenantUserDto(Guid Id, string DisplayName, string? JobTitle, IReadOnlyList<string> Roles);
