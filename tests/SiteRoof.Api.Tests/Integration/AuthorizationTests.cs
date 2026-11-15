using System.Net;
using System.Net.Http.Json;
using SiteRoof.Api.Tests.Infrastructure;

namespace SiteRoof.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests(SiteRoofFactory factory)
{
    [Fact]
    public async Task Crew_member_cannot_view_another_project_by_changing_the_url_id()
    {
        var manager = factory.CreateApiClient();
        await manager.StartDemoAsync("manager");
        var northgate = await manager.ProjectIdAsync("NTS");
        var northgateWo = await manager.WorkOrderByKeyAsync(northgate, "NTS-003");
        var woId = northgateWo.GetProperty("id").GetGuid();

        // Same tenant, crew persona (member of Riverbend only).
        await manager.SwitchPersonaAsync("crew");
        var crew = manager;

        var projects = await crew.GetJsonAsync("/api/projects");
        Assert.Equal(new[] { "RBL" }, projects.EnumerateArray().Select(p => p.GetProperty("code").GetString()!).ToArray());

        foreach (var url in new[]
                 {
                     $"/api/projects/{northgate}",
                     $"/api/projects/{northgate}/work-orders",
                     $"/api/projects/{northgate}/dashboard",
                     $"/api/projects/{northgate}/activity",
                     $"/api/projects/{northgate}/blocker-summary",
                     $"/api/work-orders/{woId}",
                     $"/api/work-orders/{woId}/material-requests",
                 })
        {
            var res = await crew.Http.GetAsync(url);
            var problem = await res.Problem(HttpStatusCode.Forbidden);
            Assert.Equal("forbidden", problem.Code());
        }

        // Writes are rejected too.
        var progress = await crew.PostAsync($"/api/work-orders/{woId}/progress", new { quantity = 1 });
        await progress.Problem(HttpStatusCode.Forbidden);

        // Activity and approvals lists never leak Northgate rows.
        var activity = await crew.GetJsonAsync("/api/activity?pageSize=100");
        Assert.DoesNotContain(activity.GetProperty("items").EnumerateArray(), a => a.GetProperty("projectCode").GetString() == "NTS");
        var approvals = await crew.GetJsonAsync("/api/material-requests?pageSize=100");
        Assert.DoesNotContain(approvals.GetProperty("items").EnumerateArray(), a => a.GetProperty("projectCode").GetString() == "NTS");
    }

    [Fact]
    public async Task Another_tenants_ids_are_invisible()
    {
        var visitorA = factory.CreateApiClient();
        await visitorA.StartDemoAsync("manager");
        var visitorB = factory.CreateApiClient();
        await visitorB.StartDemoAsync("manager");

        var projectB = await visitorB.ProjectIdAsync("RBL");
        var woB = (await visitorB.WorkOrderByKeyAsync(projectB, "RBL-007")).GetProperty("id").GetGuid();
        var pendingB = (await visitorB.GetJsonAsync("/api/material-requests?status=Submitted")).GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.NotEqual(await visitorA.ProjectIdAsync("RBL"), projectB);
        await (await visitorA.Http.GetAsync($"/api/projects/{projectB}")).Problem(HttpStatusCode.NotFound);
        await (await visitorA.Http.GetAsync($"/api/work-orders/{woB}")).Problem(HttpStatusCode.NotFound);
        var decision = await visitorA.PostAsync($"/api/material-requests/{pendingB}/decision", new { decision = "Approve" });
        await decision.Problem(HttpStatusCode.NotFound);

        var stillPending = await factory.WithDbAsync(db => Task.FromResult(db.MaterialRequests.All().Single(m => m.Id == pendingB).Status));
        Assert.Equal(Domain.MaterialRequestStatus.Submitted, stillPending);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var anon = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync("/api/projects")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync("/api/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Http.GetAsync("/api/integrations/health")).StatusCode);
    }

    [Fact]
    public async Task Unsafe_requests_without_the_csrf_header_are_rejected()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("manager");
        var rbl = await client.ProjectIdAsync("RBL");
        client.SendCsrf = false;
        var res = await client.PostAsync($"/api/projects/{rbl}/milestones", new { name = "Should not be created", dueDate = "2027-01-01" });
        var problem = await res.Problem(HttpStatusCode.BadRequest);
        Assert.Equal("csrf-invalid", problem.Code());
    }

    [Fact]
    public async Task Role_capabilities_are_enforced_server_side()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("crew");
        var rbl = await client.ProjectIdAsync("RBL");

        var create = await client.PostAsync($"/api/projects/{rbl}/work-orders",
            new { title = "Crew should not create this", workPackage = "Drywall", dueDate = "2027-01-01" });
        await create.Problem(HttpStatusCode.Forbidden);

        await (await client.Http.GetAsync("/api/admin/tenant")).Problem(HttpStatusCode.Forbidden);

        await client.SwitchPersonaAsync("supervisor");
        await (await client.Http.GetAsync("/api/admin/tenant")).Problem(HttpStatusCode.Forbidden);
        var milestone = await client.PostAsync($"/api/projects/{rbl}/milestones", new { name = "Supervisor milestone", dueDate = "2027-01-01" });
        await milestone.Problem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Demo_persona_switch_stays_inside_the_visitors_tenant()
    {
        var client = factory.CreateApiClient();
        var start = await client.StartDemoAsync("manager");
        var tenant = start.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        foreach (var persona in new[] { "supervisor", "crew", "manager" })
        {
            var session = await client.SwitchPersonaAsync(persona);
            Assert.Equal(tenant, session.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid());
            Assert.Equal(persona, session.GetProperty("user").GetProperty("persona").GetString());
        }
        var bad = await client.PostAsync("/api/demo/persona", new { persona = "admin" });
        await bad.Problem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Password_login_works_for_named_accounts_and_rejects_bad_passwords()
    {
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        await factory.WithDbAsync(async db =>
        {
            var tenant = new Domain.Tenant { Id = Guid.NewGuid(), Name = "Named workspace", CreatedAt = DateTimeOffset.UtcNow };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Domain.AppUser>>();
            var user = new Domain.AppUser { TenantId = tenant.Id, UserName = email, Email = email, DisplayName = "Workspace Owner", CreatedAt = DateTimeOffset.UtcNow };
            Assert.True((await users.CreateAsync(user, "Correct-Horse-Battery-9")).Succeeded);
            await users.AddToRoleAsync(user, Domain.TenantRoles.Admin);
        });

        var client = factory.CreateApiClient();
        await client.Http.GetAsync("/api/auth/me");
        await (await client.PostAsync("/api/auth/login", new { email, password = "wrong-password-123" })).Problem(HttpStatusCode.Unauthorized);
        var ok = await client.PostJsonAsync("/api/auth/login", new { email, password = "Correct-Horse-Battery-9" });
        Assert.True(ok.GetProperty("authenticated").GetBoolean());
        var projects = await client.GetJsonAsync("/api/projects");
        Assert.Equal(0, projects.GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/api/projects")).StatusCode);
    }
}
