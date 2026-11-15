using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteRoof.Api.Common;

namespace SiteRoof.Api.Features.Materials;

[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class MaterialRequestsController(MaterialRequestService materials) : ControllerBase
{
    /// <summary>Approvals queue across accessible projects (pending first) with ERP transmission status.</summary>
    [HttpGet("material-requests")]
    public Task<PagedResult<MaterialRequestDto>> List([FromQuery] MaterialRequestQuery query, CancellationToken ct) =>
        materials.ListAsync(query, ct);

    [HttpGet("work-orders/{workOrderId:guid}/material-requests")]
    public Task<IReadOnlyList<MaterialRequestDto>> ForWorkOrder(Guid workOrderId, CancellationToken ct) =>
        materials.ListForWorkOrderAsync(workOrderId, ct);

    /// <summary>Submit a request. 201 when created; 200 with the original when the same clientRequestId is re-sent.</summary>
    [HttpPost("work-orders/{workOrderId:guid}/material-requests")]
    [ProducesResponseType<MaterialRequestDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<MaterialRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MaterialRequestDto>> Submit(Guid workOrderId, CreateMaterialRequest req, CancellationToken ct)
    {
        var result = await materials.SubmitAsync(workOrderId, req, ct);
        return result.Created ? StatusCode(StatusCodes.Status201Created, result.Request) : Ok(result.Request);
    }

    /// <summary>Approve or reject (project manager only). Idempotent for a repeated identical decision.</summary>
    [HttpPost("material-requests/{id:guid}/decision")]
    public Task<MaterialRequestDto> Decide(Guid id, DecisionRequest req, CancellationToken ct) => materials.DecideAsync(id, req, ct);
}
