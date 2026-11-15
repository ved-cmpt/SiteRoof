using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Tests.Infrastructure;

namespace SiteRoof.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class ApprovalTests(SiteRoofFactory factory)
{
    private static async Task<JsonElement> PendingBoardRequest(TestApiClient client)
    {
        var list = await client.GetJsonAsync("/api/material-requests?status=Submitted&pageSize=100");
        return list.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("workOrderKey").GetString() == "RBL-007");
    }

    [Fact]
    public async Task Manager_approval_is_audited_idempotent_and_writes_one_outbox_event()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("manager");
        var request = await PendingBoardRequest(client);
        var id = request.GetProperty("id").GetGuid();
        Assert.True(request.GetProperty("canDecide").GetBoolean());

        var first = await client.PostJsonAsync($"/api/material-requests/{id}/decision", new { decision = "Approve", note = "Order from the yard." });
        Assert.Equal("Approved", first.GetProperty("status").GetString());
        Assert.Equal("Queued", first.GetProperty("erp").GetProperty("status").GetString());

        // Same decision again (double click, retry after timeout): no-op, same result.
        var again = await client.PostJsonAsync($"/api/material-requests/{id}/decision", new { decision = "Approve" });
        Assert.Equal("Approved", again.GetProperty("status").GetString());
        Assert.Equal(first.GetProperty("decidedAt").GetString(), again.GetProperty("decidedAt").GetString());

        // Contradicting it is a conflict, not a silent overwrite.
        var reject = await client.PostAsync($"/api/material-requests/{id}/decision", new { decision = "Reject", note = "Changed my mind" });
        Assert.Equal("already-decided", (await reject.Problem(HttpStatusCode.Conflict)).Code());

        await factory.WithDbAsync(async db =>
        {
            var audits = await db.AuditEvents.All().Where(a => a.EntityId == id && a.Action == AuditActions.Approved).ToListAsync();
            var approval = Assert.Single(audits);
            Assert.NotNull(approval.ActorId);
            Assert.Contains("Submitted", approval.BeforeJson);
            Assert.Contains("Approved", approval.AfterJson);
            Assert.Single(await db.IntegrationOutbox.All().Where(o => o.AggregateId == id).ToListAsync());
            var mr = await db.MaterialRequests.All().SingleAsync(m => m.Id == id);
            Assert.Equal(MaterialRequestStatus.Approved, mr.Status);
            Assert.Equal(approval.ActorId, mr.DecidedById);
        });
    }

    [Fact]
    public async Task Crew_cannot_approve_and_nothing_changes()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("crew");
        var request = await PendingBoardRequest(client);
        var id = request.GetProperty("id").GetGuid();
        Assert.False(request.GetProperty("canDecide").GetBoolean());

        var res = await client.PostAsync($"/api/material-requests/{id}/decision", new { decision = "Approve" });
        Assert.Equal("forbidden", (await res.Problem(HttpStatusCode.Forbidden)).Code());

        await client.SwitchPersonaAsync("supervisor");
        await (await client.PostAsync($"/api/material-requests/{id}/decision", new { decision = "Approve" })).Problem(HttpStatusCode.Forbidden);

        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(MaterialRequestStatus.Submitted, (await db.MaterialRequests.All().SingleAsync(m => m.Id == id)).Status);
            Assert.False(await db.IntegrationOutbox.All().AnyAsync(o => o.AggregateId == id));
            Assert.False(await db.AuditEvents.All().AnyAsync(a => a.EntityId == id && a.Action == AuditActions.Approved));
        });
    }

    [Fact]
    public async Task Rejection_requires_a_reason_and_creates_no_outbox_event()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("manager");
        var id = (await PendingBoardRequest(client)).GetProperty("id").GetGuid();

        var noReason = await client.PostAsync($"/api/material-requests/{id}/decision", new { decision = "Reject" });
        Assert.Equal("validation", (await noReason.Problem(HttpStatusCode.BadRequest)).Code());

        var rejected = await client.PostJsonAsync($"/api/material-requests/{id}/decision", new { decision = "Reject", note = "Use yard stock." });
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("NotApplicable", rejected.GetProperty("erp").GetProperty("status").GetString());
        await factory.WithDbAsync(async db => Assert.False(await db.IntegrationOutbox.All().AnyAsync(o => o.AggregateId == id)));
    }

    [Fact]
    public async Task Submitting_the_same_request_twice_creates_it_once()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("crew");
        var rbl = await client.ProjectIdAsync("RBL");
        var wo = await client.WorkOrderByKeyAsync(rbl, "RBL-006");
        var woId = wo.GetProperty("id").GetGuid();
        var body = new { clientRequestId = Guid.NewGuid(), itemName = "Drywall screws 1-5/8\"", quantity = 10, unit = "boxes" };

        var first = await client.PostAsync($"/api/work-orders/{woId}/material-requests", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await client.PostAsync($"/api/work-orders/{woId}/material-requests", body);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<JsonElement>();
        var b = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());

        await factory.WithDbAsync(async db =>
            Assert.Equal(1, await db.MaterialRequests.All().CountAsync(m => m.ClientRequestId == body.clientRequestId)));

        var negative = await client.PostAsync($"/api/work-orders/{woId}/material-requests",
            new { clientRequestId = Guid.NewGuid(), itemName = "Bad", quantity = -5, unit = "ea" });
        await negative.Problem(HttpStatusCode.BadRequest);
    }
}
