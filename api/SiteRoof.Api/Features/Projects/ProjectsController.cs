using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Projects;

[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class ProjectsController(ProjectService projects) : ControllerBase
{
    /// <summary>Projects the caller can access, with completion rollups from vw_ProjectWorkOrderRollup.</summary>
    [HttpGet("projects")]
    public Task<IReadOnlyList<ProjectSummaryDto>> List([FromQuery] ProjectStatus? status, [FromQuery] string? search, CancellationToken ct) =>
        projects.ListAsync(status, search, ct);

    [HttpPost("projects")]
    [ProducesResponseType<ProjectDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ProjectDetailDto>> Create(CreateProjectRequest req, CancellationToken ct)
    {
        var created = await projects.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("projects/{id:guid}")]
    public Task<ProjectDetailDto> Get(Guid id, CancellationToken ct) => projects.GetAsync(id, ct);

    [HttpPatch("projects/{id:guid}")]
    public Task<ProjectDetailDto> Update(Guid id, UpdateProjectRequest req, CancellationToken ct) => projects.UpdateAsync(id, req, ct);

    [HttpPost("projects/{id:guid}/members")]
    public Task<ProjectDetailDto> AddMember(Guid id, AddMemberRequest req, CancellationToken ct) => projects.AddMemberAsync(id, req, ct);

    [HttpGet("projects/{id:guid}/milestones")]
    public Task<IReadOnlyList<MilestoneDto>> Milestones(Guid id, CancellationToken ct) => projects.ListMilestonesAsync(id, ct);

    [HttpPost("projects/{id:guid}/milestones")]
    [ProducesResponseType<MilestoneDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MilestoneDto>> CreateMilestone(Guid id, CreateMilestoneRequest req, CancellationToken ct)
    {
        var created = await projects.CreateMilestoneAsync(id, req, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPatch("milestones/{id:guid}")]
    public Task<MilestoneDto> UpdateMilestone(Guid id, UpdateMilestoneRequest req, CancellationToken ct) =>
        projects.UpdateMilestoneAsync(id, req, ct);

    /// <summary>Users in the caller's workspace (for roster management).</summary>
    [HttpGet("users")]
    public Task<IReadOnlyList<TenantUserDto>> Users(CancellationToken ct) => projects.ListTenantUsersAsync(ct);
}
