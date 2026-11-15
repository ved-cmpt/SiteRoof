using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Tests.Infrastructure;

namespace SiteRoof.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class WorkOrderTests(SiteRoofFactory factory)
{
    private static Guid UserId(JsonElement session) => session.GetProperty("user").GetProperty("id").GetGuid();

    [Fact]
    public async Task Conflicting_status_edits_resolve_safely_with_a_visible_409()
    {
        var supervisor = factory.CreateApiClient();
        await supervisor.StartDemoAsync("supervisor");
        var rbl = await supervisor.ProjectIdAsync("RBL");
        var wo = await supervisor.WorkOrderByKeyAsync(rbl, "RBL-006");
        var id = wo.GetProperty("id").GetGuid();
        var staleVersion = wo.GetProperty("version").GetString();

        // First editor blocks the work order.
        var first = await supervisor.PostJsonAsync($"/api/work-orders/{id}/transitions",
            new { transition = "Block", version = staleVersion, reason = "Board delivery missed" });
        Assert.Equal("Blocked", first.GetProperty("status").GetString());

        // Second editor, still holding the old version, tries to complete it.
        var second = await supervisor.PostAsync($"/api/work-orders/{id}/transitions", new { transition = "Complete", version = staleVersion });
        var problem = await second.Problem(HttpStatusCode.Conflict);
        Assert.Equal("concurrency-conflict", problem.Code());
        Assert.Contains("changed by someone else", problem.GetProperty("detail").GetString());
        Assert.Equal("Blocked", problem.GetProperty("currentStatus").GetString());

        // A stale field edit is rejected the same way.
        var patch = await supervisor.PatchAsync($"/api/work-orders/{id}", new { version = staleVersion, priority = "Low" });
        Assert.Equal("concurrency-conflict", (await patch.Problem(HttpStatusCode.Conflict)).Code());

        await factory.WithDbAsync(async db =>
        {
            var row = await db.WorkOrders.All().SingleAsync(w => w.Id == id);
            Assert.Equal(WorkOrderStatus.Blocked, row.Status);
            Assert.Equal(WorkOrderPriority.Critical, row.Priority);
        });
    }

    [Fact]
    public async Task Creating_and_completing_work_updates_dashboard_database_and_activity()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("crew");
        var crewId = UserId(await client.GetJsonAsync("/api/auth/me"));
        await client.SwitchPersonaAsync("supervisor");
        var rbl = await client.ProjectIdAsync("RBL");
        var before = await client.GetJsonAsync($"/api/projects/{rbl}/dashboard");
        var beforeKpis = before.GetProperty("kpis");

        // Supervisor creates and assigns to an eligible crew member.
        var created = await client.PostJsonAsync($"/api/projects/{rbl}/work-orders", new
        {
            title = "Patch drywall at return air grilles", workPackage = "Drywall", assigneeId = crewId,
            dueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)).ToString("yyyy-MM-dd"), priority = "High", plannedQuantity = 12, unit = "m²",
        });
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("RBL-016", created.GetProperty("key").GetString());

        var afterCreate = (await client.GetJsonAsync($"/api/projects/{rbl}/dashboard")).GetProperty("kpis");
        Assert.Equal(beforeKpis.GetProperty("total").GetInt32() + 1, afterCreate.GetProperty("total").GetInt32());
        Assert.Equal(beforeKpis.GetProperty("open").GetInt32() + 1, afterCreate.GetProperty("open").GetInt32());

        // Crew reports progress (auto-starts) and completes.
        await client.SwitchPersonaAsync("crew");
        var progressed = await client.PostJsonAsync($"/api/work-orders/{id}/progress", new { quantity = 12, note = "All grilles patched." });
        Assert.Equal("InProgress", progressed.GetProperty("status").GetString());
        var completed = await client.PostJsonAsync($"/api/work-orders/{id}/transitions",
            new { transition = "Complete", version = progressed.GetProperty("version").GetString() });
        Assert.Equal("Completed", completed.GetProperty("status").GetString());

        var afterComplete = (await client.GetJsonAsync($"/api/projects/{rbl}/dashboard")).GetProperty("kpis");
        Assert.Equal(beforeKpis.GetProperty("done").GetInt32() + 1, afterComplete.GetProperty("done").GetInt32());
        Assert.Equal(beforeKpis.GetProperty("open").GetInt32(), afterComplete.GetProperty("open").GetInt32());
        var expectedPercent = (int)Math.Round(100.0 * afterComplete.GetProperty("done").GetInt32() / afterComplete.GetProperty("total").GetInt32());
        Assert.Equal(expectedPercent, afterComplete.GetProperty("percentComplete").GetInt32());

        await factory.WithDbAsync(async db =>
        {
            var row = await db.WorkOrders.All().SingleAsync(w => w.Id == id);
            Assert.Equal(WorkOrderStatus.Completed, row.Status);
            Assert.NotNull(row.CompletedAt);
            Assert.Equal(12m, await db.ProgressLogs.All().Where(p => p.WorkOrderId == id).SumAsync(p => p.Quantity));
        });

        var feed = await client.GetJsonAsync($"/api/projects/{rbl}/activity?pageSize=50");
        var summaries = feed.GetProperty("items").EnumerateArray()
            .Where(a => a.GetProperty("link").ValueKind == JsonValueKind.Object && a.GetProperty("link").GetProperty("id").GetGuid() == id)
            .Select(a => a.GetProperty("summary").GetString()!).ToList();
        Assert.Contains(summaries, s => s.StartsWith("Created RBL-016"));
        Assert.Contains(summaries, s => s.StartsWith("Assigned RBL-016 to Leah Tremblay"));
        Assert.Contains(summaries, s => s.Contains("from In progress to Completed"));
        var statusChange = feed.GetProperty("items").EnumerateArray().First(a => a.GetProperty("summary").GetString()!.Contains("to Completed"));
        Assert.Equal("InProgress", statusChange.GetProperty("before").GetProperty("status").GetString());
        Assert.Equal("Completed", statusChange.GetProperty("after").GetProperty("status").GetString());
        Assert.Equal("Leah Tremblay", statusChange.GetProperty("actorName").GetString());
    }

    [Fact]
    public async Task Business_rules_are_enforced()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("supervisor");
        var rbl = await client.ProjectIdAsync("RBL");
        var nts = await client.ProjectIdAsync("MCC");
        var northgateOnlyUser = (await client.GetJsonAsync("/api/users")).EnumerateArray()
            .Single(u => u.GetProperty("displayName").GetString() == "Hannah Lindqvist").GetProperty("id").GetGuid();

        // Assignment must stay within the project's roster.
        var outsider = await client.PostAsync($"/api/projects/{rbl}/work-orders",
            new { title = "Assign to outsider", workPackage = "Glazing", dueDate = "2027-02-01", assigneeId = northgateOnlyUser });
        Assert.Equal("validation", (await outsider.Problem(HttpStatusCode.BadRequest)).Code());

        // Cannot complete while a dependency is open (RBL-008 depends on RBL-006).
        var taping = await client.WorkOrderByKeyAsync(rbl, "RBL-008");
        var started = await client.PostJsonAsync($"/api/work-orders/{taping.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Start", version = taping.GetProperty("version").GetString() });
        var complete = await client.PostAsync($"/api/work-orders/{taping.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Complete", version = started.GetProperty("version").GetString() });
        var dep = await complete.Problem(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("dependencies-incomplete", dep.Code());
        Assert.Contains("RBL-006", dep.GetProperty("detail").GetString());

        // Dependency cycles are refused (RBL-008 -> RBL-006 exists; RBL-006 -> RBL-008 would loop).
        var drywall = await client.WorkOrderByKeyAsync(rbl, "RBL-006");
        var cycle = await client.PostAsync($"/api/work-orders/{drywall.GetProperty("id").GetGuid()}/dependencies",
            new { dependsOnWorkOrderId = taping.GetProperty("id").GetGuid() });
        Assert.Equal("dependency-cycle", (await cycle.Problem(HttpStatusCode.UnprocessableEntity)).Code());

        // Closing requires verified progress: RBL-005 is completed but has an unverified log.
        var hvac = await client.WorkOrderByKeyAsync(rbl, "RBL-005");
        var close = await client.PostAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Close", version = hvac.GetProperty("version").GetString() });
        Assert.Equal("close-preconditions", (await close.Problem(HttpStatusCode.UnprocessableEntity)).Code());

        var log = hvac.GetProperty("progress")[0].GetProperty("id").GetGuid();
        var verified = await client.PostJsonAsync($"/api/progress/{log}/verify", new { });
        await (await client.PostAsync($"/api/progress/{log}/verify", new { })).Problem(HttpStatusCode.Conflict);
        var closed = await client.PostJsonAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Close", version = verified.GetProperty("version").GetString() });
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.True(closed.GetProperty("isLocked").GetBoolean());

        // Closed work is locked against silent edits and new comments.
        var edit = await client.PatchAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}",
            new { version = closed.GetProperty("version").GetString(), title = "Sneaky edit" });
        Assert.Equal("work-order-locked", (await edit.Problem(HttpStatusCode.Conflict)).Code());
        var comment = await client.PostAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}/comments", new { body = "late note" });
        Assert.Equal("work-order-locked", (await comment.Problem(HttpStatusCode.Conflict)).Code());

        // Only a manager can reopen closed work, and only with a reason.
        var reopen = await client.PostAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Reopen", version = closed.GetProperty("version").GetString(), reason = "Balancing report missing" });
        await reopen.Problem(HttpStatusCode.Forbidden);
        await client.SwitchPersonaAsync("manager");
        var noReason = await client.PostAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Reopen", version = closed.GetProperty("version").GetString() });
        await noReason.Problem(HttpStatusCode.BadRequest);
        var reopened = await client.PostJsonAsync($"/api/work-orders/{hvac.GetProperty("id").GetGuid()}/transitions",
            new { transition = "Reopen", version = closed.GetProperty("version").GetString(), reason = "Balancing report missing" });
        Assert.Equal("InProgress", reopened.GetProperty("status").GetString());

        // Completed project accepts no new work.
        var onCompleted = await client.PostAsync($"/api/projects/{nts}/work-orders",
            new { title = "Too late", workPackage = "Finishes", dueDate = "2027-02-01" });
        Assert.Equal("project-completed", (await onCompleted.Problem(HttpStatusCode.UnprocessableEntity)).Code());
    }

    [Fact]
    public async Task Progress_validation_blockers_and_verification()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("crew");
        var rbl = await client.ProjectIdAsync("RBL");
        var wo = await client.WorkOrderByKeyAsync(rbl, "RBL-006");
        var id = wo.GetProperty("id").GetGuid();

        await (await client.PostAsync($"/api/work-orders/{id}/progress", new { quantity = -3 })).Problem(HttpStatusCode.BadRequest);
        await (await client.PostAsync($"/api/work-orders/{id}/progress", new { quantity = 0 })).Problem(HttpStatusCode.BadRequest);

        // Crew cannot report on work assigned to someone else (RBL-007 belongs to Rosa).
        var other = await client.WorkOrderByKeyAsync(rbl, "RBL-007");
        await (await client.PostAsync($"/api/work-orders/{other.GetProperty("id").GetGuid()}/progress", new { quantity = 5 })).Problem(HttpStatusCode.Forbidden);

        // Reporting a blocker blocks the work order.
        var reported = await client.PostJsonAsync($"/api/work-orders/{id}/progress",
            new { quantity = 30, blocker = "Out of joint compound until delivery." });
        Assert.Equal("Blocked", reported.GetProperty("status").GetString());
        var logId = reported.GetProperty("progress")[0].GetProperty("id").GetGuid();

        // The crew member cannot verify; the supervisor can, and can resolve the blocker which unblocks the work.
        await (await client.PostAsync($"/api/progress/{logId}/verify", new { })).Problem(HttpStatusCode.Forbidden);
        await client.SwitchPersonaAsync("supervisor");
        var verified = await client.PostJsonAsync($"/api/progress/{logId}/verify", new { });
        Assert.Equal("Marcus Oyelaran", verified.GetProperty("progress")[0].GetProperty("verifiedBy").GetProperty("displayName").GetString());
        var resolved = await client.PostJsonAsync($"/api/progress/{logId}/resolve-blocker", new { });
        Assert.Equal("InProgress", resolved.GetProperty("status").GetString());

        // A supervisor cannot verify their own report.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var own = await client.PostJsonAsync($"/api/work-orders/{id}/progress", new { quantity = 5, note = "Supervisor helped." });
        var ownLog = own.GetProperty("progress")[0].GetProperty("id").GetGuid();
        Assert.Equal("self-verification", (await (await client.PostAsync($"/api/progress/{ownLog}/verify", new { })).Problem(HttpStatusCode.UnprocessableEntity)).Code());
    }
}
