using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteRoof.Api.Common;

namespace SiteRoof.Api.Features.WorkOrders;

[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class WorkOrdersController(WorkOrderService workOrders) : ControllerBase
{
    /// <summary>Work-order board for a project with filtering, sorting and pagination.</summary>
    [HttpGet("projects/{projectId:guid}/work-orders")]
    public Task<PagedResult<WorkOrderListItemDto>> List(Guid projectId, [FromQuery] WorkOrderQuery query, CancellationToken ct) =>
        workOrders.ListAsync(projectId, query, ct);

    [HttpPost("projects/{projectId:guid}/work-orders")]
    [ProducesResponseType<WorkOrderDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkOrderDetailDto>> Create(Guid projectId, CreateWorkOrderRequest req, CancellationToken ct)
    {
        var created = await workOrders.CreateAsync(projectId, req, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Active work assigned to the signed-in user across projects.</summary>
    [HttpGet("work-orders/mine")]
    public Task<IReadOnlyList<WorkOrderListItemDto>> Mine(CancellationToken ct) => workOrders.ListMineAsync(ct);

    [HttpGet("work-orders/{id:guid}")]
    public Task<WorkOrderDetailDto> Get(Guid id, CancellationToken ct) => workOrders.GetAsync(id, ct);

    /// <summary>Edit fields. Requires the current <c>version</c>; a stale version returns 409.</summary>
    [HttpPatch("work-orders/{id:guid}")]
    public Task<WorkOrderDetailDto> Update(Guid id, UpdateWorkOrderRequest req, CancellationToken ct) => workOrders.UpdateAsync(id, req, ct);

    /// <summary>Status transition (start, block, unblock, complete, reopen, close, cancel).</summary>
    [HttpPost("work-orders/{id:guid}/transitions")]
    public Task<WorkOrderDetailDto> Transition(Guid id, TransitionRequest req, CancellationToken ct) => workOrders.TransitionAsync(id, req, ct);

    [HttpGet("work-orders/{id:guid}/comments")]
    public Task<IReadOnlyList<CommentDto>> Comments(Guid id, CancellationToken ct) => workOrders.ListCommentsAsync(id, ct);

    [HttpPost("work-orders/{id:guid}/comments")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, CreateCommentRequest req, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await workOrders.AddCommentAsync(id, req, ct));

    [HttpPost("work-orders/{id:guid}/dependencies")]
    public Task<WorkOrderDetailDto> AddDependency(Guid id, AddDependencyRequest req, CancellationToken ct) =>
        workOrders.AddDependencyAsync(id, req, ct);

    [HttpDelete("work-orders/{id:guid}/dependencies/{dependsOnId:guid}")]
    public Task<WorkOrderDetailDto> RemoveDependency(Guid id, Guid dependsOnId, CancellationToken ct) =>
        workOrders.RemoveDependencyAsync(id, dependsOnId, ct);

    [HttpGet("work-orders/{id:guid}/progress")]
    public Task<IReadOnlyList<ProgressLogDto>> Progress(Guid id, CancellationToken ct) => workOrders.ListProgressAsync(id, ct);

    /// <summary>Report quantity installed, an optional blocker and a note.</summary>
    [HttpPost("work-orders/{id:guid}/progress")]
    [ProducesResponseType<WorkOrderDetailDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkOrderDetailDto>> AddProgress(Guid id, CreateProgressRequest req, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await workOrders.AddProgressAsync(id, req, ct));

    [HttpPost("progress/{id:guid}/verify")]
    public Task<WorkOrderDetailDto> Verify(Guid id, CancellationToken ct) => workOrders.VerifyProgressAsync(id, ct);

    [HttpPost("progress/{id:guid}/resolve-blocker")]
    public Task<WorkOrderDetailDto> ResolveBlocker(Guid id, CancellationToken ct) => workOrders.ResolveBlockerAsync(id, ct);
}
