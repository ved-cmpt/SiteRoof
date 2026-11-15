using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Integrations;
using SiteRoof.Api.Tests.Infrastructure;

namespace SiteRoof.Api.Tests.Integration;

/// <summary>
/// Outbox tests share the factory's fake clock and drive the processor directly, so they run sequentially
/// in their own collection-ordered class and only assert on their own tenant's rows.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ErpIntegrationTests(SiteRoofFactory factory)
{
    private async Task<(TestApiClient Client, Guid RequestId, Guid TenantId)> ApproveOneAsync(ErpSimulatorMode mode)
    {
        var client = factory.CreateApiClient();
        var session = await client.StartDemoAsync("manager");
        var tenantId = session.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        await client.PatchAsync("/api/admin/tenant", new { erpSimulatorMode = mode.ToString() }).ContinueWith(t => t.Result.EnsureOk()).Unwrap();
        var list = await client.GetJsonAsync("/api/material-requests?status=Submitted&pageSize=100");
        var id = list.GetProperty("items").EnumerateArray().First(m => m.GetProperty("workOrderKey").GetString() == "RBL-007").GetProperty("id").GetGuid();
        await client.PostJsonAsync($"/api/material-requests/{id}/decision", new { decision = "Approve" });
        return (client, id, tenantId);
    }

    private Task<IntegrationOutbox> OutboxFor(Guid requestId) =>
        factory.WithDbAsync(db => db.IntegrationOutbox.All().AsNoTracking().SingleAsync(o => o.AggregateId == requestId));

    [Fact]
    public async Task Erp_returns_503_once_then_the_retry_succeeds_without_a_duplicate()
    {
        var (client, id, _) = await ApproveOneAsync(ErpSimulatorMode.FailFirstAttempt);
        var key = (await OutboxFor(id)).IdempotencyKey;

        // Attempt 1: the mock ERP answers 503; the event is rescheduled with backoff.
        await factory.RunOutboxAsync();
        var afterFirst = await OutboxFor(id);
        Assert.Equal(OutboxStatus.Pending, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        Assert.StartsWith("503", afterFirst.LastError);
        Assert.True(afterFirst.NextRetryAt > factory.Clock.GetUtcNow());

        var mid = await client.GetJsonAsync($"/api/material-requests?status=Approved&pageSize=100");
        var erp = mid.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == id).GetProperty("erp");
        Assert.Equal("Retrying", erp.GetProperty("status").GetString());

        // Not due yet: nothing is re-sent early.
        await factory.RunOutboxAsync();
        Assert.Equal(1, (await OutboxFor(id)).Attempts);

        // Attempt 2 after the backoff: success, external reference stored on the request.
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        await factory.RunOutboxAsync();
        var done = await OutboxFor(id);
        Assert.Equal(OutboxStatus.Succeeded, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.NotNull(done.ExternalReference);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.MockErpRequisitions.CountAsync(r => r.IdempotencyKey == key));
            var calls = await db.MockErpCalls.Where(c => c.IdempotencyKey == key).OrderBy(c => c.Id).Select(c => c.ResponseStatus).ToListAsync();
            Assert.Equal(new[] { 503, 200 }, calls);
            var mr = await db.MaterialRequests.All().SingleAsync(m => m.Id == id);
            Assert.Equal(done.ExternalReference, mr.ExternalReference);
            var actions = await db.AuditEvents.All().Where(a => a.EntityId == id).Select(a => a.Action).ToListAsync();
            Assert.Contains(AuditActions.ErpRetryScheduled, actions);
            Assert.Contains(AuditActions.ErpAcknowledged, actions);
        });

        // A redelivery with the same key (e.g. worker crashed after send, before commit) is de-duplicated by the ERP.
        using var http = factory.CreateClient();
        var resend = new HttpRequestMessage(HttpMethod.Post, "/mock-erp/requisitions") { Content = JsonContent.Create(new { tenantId = done.TenantId }) };
        resend.Headers.Add("Idempotency-Key", key);
        resend.Headers.Add(HttpErpClient.KeyHeader, "test-erp-key");
        var ack = await (await http.SendAsync(resend)).Content.ReadFromJsonAsync<ErpAcknowledgement>();
        Assert.True(ack!.Duplicate);
        Assert.Equal(done.ExternalReference, ack.ExternalReference);
        await factory.WithDbAsync(async db => Assert.Equal(1, await db.MockErpRequisitions.CountAsync(r => r.IdempotencyKey == key)));

        var health = await client.GetJsonAsync("/api/integrations/health");
        Assert.True(health.GetProperty("mockErpReachable").GetBoolean());
        Assert.True(health.GetProperty("outbox").GetProperty("succeeded").GetInt32() >= 1);
    }

    [Fact]
    public async Task Retries_are_bounded_and_a_failed_event_can_be_requeued_by_the_manager()
    {
        var (client, id, _) = await ApproveOneAsync(ErpSimulatorMode.Unavailable);
        for (var i = 0; i < 5; i++)
        {
            await factory.RunOutboxAsync();
            factory.Clock.Advance(TimeSpan.FromMinutes(6));
        }
        var failed = await OutboxFor(id);
        Assert.Equal(OutboxStatus.Failed, failed.Status);
        Assert.Equal(5, failed.Attempts);

        // Further runs never touch a dead-lettered event.
        await factory.RunOutboxAsync();
        Assert.Equal(5, (await OutboxFor(id)).Attempts);

        await client.SwitchPersonaAsync("crew");
        await (await client.PostAsync($"/api/integrations/outbox/{failed.Id}/retry", new { })).Problem(HttpStatusCode.Forbidden);
        await client.SwitchPersonaAsync("manager");
        await (await client.PatchAsync("/api/admin/tenant", new { erpSimulatorMode = "Normal" })).EnsureOk();
        await client.PostJsonAsync($"/api/integrations/outbox/{failed.Id}/retry", new { });
        await factory.RunOutboxAsync();
        var sent = await OutboxFor(id);
        Assert.Equal(OutboxStatus.Succeeded, sent.Status);
        await factory.WithDbAsync(async db => Assert.Equal(1, await db.MockErpRequisitions.CountAsync(r => r.IdempotencyKey == sent.IdempotencyKey)));
    }

    [Fact]
    public async Task Mock_erp_rejects_calls_without_the_shared_key()
    {
        using var http = factory.CreateClient();
        var res = await http.PostAsJsonAsync("/mock-erp/requisitions", new { tenantId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
