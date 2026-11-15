using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Integrations;

/// <summary>
/// Self-hosted mock ERP purchase-requisition endpoint. It is deliberately separate from the SiteRoof
/// domain: it has its own schema (<c>mockerp</c>), is reached over HTTP by the outbox worker, requires
/// a shared key and de-duplicates by <c>Idempotency-Key</c>. Fault injection follows each tenant's
/// <see cref="ErpSimulatorMode"/> so retries can be demonstrated and tested.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("mock-erp")]
[ApiExplorerSettings(GroupName = "mock-erp")]
public sealed class MockErpController(SiteRoofDbContext db, IOptions<ErpOptions> options, TimeProvider time) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status() => Authorized() ? Ok(new { status = "ok", system = "SiteRoof mock ERP" }) : Unauthorized();

    [HttpPost("requisitions")]
    public async Task<IActionResult> Requisition(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        if (!Authorized()) return Unauthorized();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
        {
            return Problem(statusCode: 400, title: "Idempotency-Key header is required.");
        }
        if (!body.TryGetProperty("tenantId", out var t) || !t.TryGetGuid(out var tenantId))
        {
            return Problem(statusCode: 400, title: "tenantId is required.");
        }

        var now = time.GetUtcNow();
        var mode = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId).Select(x => (ErpSimulatorMode?)x.ErpSimulatorMode).SingleOrDefaultAsync(ct)
            ?? ErpSimulatorMode.Normal;
        var previousCalls = await db.MockErpCalls.CountAsync(c => c.IdempotencyKey == idempotencyKey, ct);

        var fail = mode == ErpSimulatorMode.Unavailable || (mode == ErpSimulatorMode.FailFirstAttempt && previousCalls == 0);
        db.MockErpCalls.Add(new MockErpCall { IdempotencyKey = idempotencyKey, TenantId = tenantId, ResponseStatus = fail ? 503 : 200, ReceivedAt = now });
        if (fail)
        {
            await db.SaveChangesAsync(ct);
            Response.Headers.RetryAfter = "5";
            return Problem(statusCode: 503, title: "ERP temporarily unavailable (simulated).");
        }

        var existing = await db.MockErpRequisitions.AsNoTracking().SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            await db.SaveChangesAsync(ct);
            return Ok(new ErpAcknowledgement(existing.ExternalReference, Duplicate: true));
        }

        var reference = $"PR-{now:yyMMdd}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)))[..6]}";
        db.MockErpRequisitions.Add(new MockErpRequisition
        {
            IdempotencyKey = idempotencyKey,
            ExternalReference = reference,
            TenantId = tenantId,
            Payload = body.GetRawText(),
            ReceivedAt = now,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent duplicate: the unique index on IdempotencyKey kept exactly one requisition.
            db.ChangeTracker.Clear();
            var winner = await db.MockErpRequisitions.AsNoTracking().SingleAsync(r => r.IdempotencyKey == idempotencyKey, ct);
            return Ok(new ErpAcknowledgement(winner.ExternalReference, Duplicate: true));
        }
        return StatusCode(201, new ErpAcknowledgement(reference, Duplicate: false));
    }

    private bool Authorized()
    {
        var supplied = Request.Headers[HttpErpClient.KeyHeader].ToString();
        var expected = options.Value.ApiKey ?? "";
        return supplied.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }
}
