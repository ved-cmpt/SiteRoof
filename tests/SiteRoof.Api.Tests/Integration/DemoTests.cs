using Microsoft.AspNetCore.Hosting;
using System.Net;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Demo;
using SiteRoof.Api.Tests.Infrastructure;

namespace SiteRoof.Api.Tests.Integration;

[Collection(ApiCollection.Name)]
public sealed class DemoTests(SiteRoofFactory factory)
{
    [Fact]
    public async Task Try_demo_provisions_the_full_seeded_scenario_in_a_private_tenant()
    {
        var client = factory.CreateApiClient();
        var session = await client.StartDemoAsync();
        var tenantId = session.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        Assert.True(session.GetProperty("user").GetProperty("tenant").GetProperty("isDemo").GetBoolean());

        await factory.WithDbAsync(async db =>
        {
            var projects = await db.Projects.All().Where(p => p.TenantId == tenantId).ToListAsync();
            Assert.Equal(new[] { "MCC", "NTS", "RBL" }, projects.Select(p => p.Code).Order().ToArray());
            Assert.Contains(projects, p => p.Status == ProjectStatus.Completed);
            Assert.True(await db.WorkOrders.All().CountAsync(w => w.TenantId == tenantId) >= 25);
            Assert.True(await db.Milestones.All().CountAsync(m => m.Project!.TenantId == tenantId) >= 10);
            Assert.Equal(5, await db.MaterialRequests.All().CountAsync(m => m.TenantId == tenantId && m.Status == MaterialRequestStatus.Submitted));
            Assert.Equal(1, await db.MaterialRequests.All().CountAsync(m => m.TenantId == tenantId && m.Status == MaterialRequestStatus.Rejected));
            Assert.Equal(1, await db.IntegrationOutbox.All().CountAsync(o => o.TenantId == tenantId && o.Status == OutboxStatus.Pending && o.Attempts > 0));
            Assert.Equal(DemoPeople.All.Length, await db.Users.CountAsync(u => u.TenantId == tenantId));
            Assert.False(await db.Users.AnyAsync(u => u.TenantId == tenantId && u.PasswordHash != null));
        });

        var overview = await client.GetJsonAsync("/api/overview");
        Assert.True(overview.GetProperty("kpis").GetProperty("overdueWorkOrders").GetInt32() >= 3);
        Assert.Equal(5, overview.GetProperty("kpis").GetProperty("pendingApprovals").GetInt32());

        var scenario = await client.GetJsonAsync("/api/demo/scenario");
        Assert.Equal(3, scenario.GetProperty("personas").GetArrayLength());
        Assert.Equal(6, scenario.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task Seeding_is_idempotent_for_an_existing_tenant()
    {
        var client = factory.CreateApiClient();
        var session = await client.StartDemoAsync();
        var tenantId = session.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();

        var counts = () => factory.WithDbAsync(async db => (
            await db.Projects.All().CountAsync(p => p.TenantId == tenantId),
            await db.WorkOrders.All().CountAsync(w => w.TenantId == tenantId),
            await db.MaterialRequests.All().CountAsync(m => m.TenantId == tenantId),
            await db.AuditEvents.All().CountAsync(a => a.TenantId == tenantId)));
        var before = await counts();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Data.SiteRoofDbContext>();
            var people = await db.Users.Where(u => u.TenantId == tenantId).ToListAsync();
            var keys = DemoPeople.All.ToDictionary(p => p.Key, p => people.Single(u => u.DisplayName == p.Name).Id);
            var seeded = await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(tenantId, keys, CancellationToken.None);
            Assert.False(seeded);
        }
        Assert.Equal(before, await counts());
    }

    [Fact]
    public async Task Visitors_are_isolated_and_reset_replaces_only_the_callers_workspace()
    {
        var a = factory.CreateApiClient();
        var b = factory.CreateApiClient();
        var tenantA = (await a.StartDemoAsync()).GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        var tenantB = (await b.StartDemoAsync()).GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        Assert.NotEqual(tenantA, tenantB);

        // Visitor A changes their data; visitor B's identical record is untouched.
        var rblA = await a.ProjectIdAsync("RBL");
        await a.PostJsonAsync($"/api/projects/{rblA}/milestones", new { name = "Visitor A milestone", dueDate = "2027-03-01" });
        var rblB = await b.ProjectIdAsync("RBL");
        var milestonesB = await b.GetJsonAsync($"/api/projects/{rblB}/milestones");
        Assert.DoesNotContain(milestonesB.EnumerateArray(), m => m.GetProperty("name").GetString() == "Visitor A milestone");

        // Starting again while in a live demo keeps the same workspace (no tenant per click).
        var again = await a.PostJsonAsync("/api/demo/start", new { persona = "manager" });
        Assert.Equal(tenantA, again.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid());

        var reset = await a.PostJsonAsync("/api/demo/reset", new { });
        var newTenant = reset.GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        Assert.NotEqual(tenantA, newTenant);
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Tenants.AnyAsync(t => t.Id == tenantA));
            Assert.False(await db.Projects.All().AnyAsync(p => p.TenantId == tenantA));
            Assert.True(await db.Tenants.AnyAsync(t => t.Id == tenantB));
        });
        var freshMilestones = await a.GetJsonAsync($"/api/projects/{await a.ProjectIdAsync("RBL")}/milestones");
        Assert.DoesNotContain(freshMilestones.EnumerateArray(), m => m.GetProperty("name").GetString() == "Visitor A milestone");
        Assert.Equal(HttpStatusCode.OK, (await b.Http.GetAsync($"/api/projects/{rblB}")).StatusCode);
    }

    [Fact]
    public async Task Expired_demo_tenants_are_cleaned_up()
    {
        var client = factory.CreateApiClient();
        var tenantId = (await client.StartDemoAsync()).GetProperty("user").GetProperty("tenant").GetProperty("id").GetGuid();
        await factory.WithDbAsync(async db =>
            await db.Tenants.Where(t => t.Id == tenantId).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))));

        var removed = await DemoCleanupService.RunOnceAsync(factory.Services.GetRequiredService<IServiceScopeFactory>(), factory.Clock, CancellationToken.None);
        Assert.True(removed >= 1);
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Tenants.AnyAsync(t => t.Id == tenantId));
            Assert.False(await db.Users.AnyAsync(u => u.TenantId == tenantId));
            Assert.False(await db.AuditEvents.All().AnyAsync(a => a.TenantId == tenantId));
        });

        // The old cookie no longer resolves to a user.
        var me = await client.GetJsonAsync("/api/auth/me");
        Assert.False(me.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Blocker_summary_is_rules_based_and_evidence_linked()
    {
        var client = factory.CreateApiClient();
        await client.StartDemoAsync("supervisor");
        var rbl = await client.ProjectIdAsync("RBL");
        var summary = await client.GetJsonAsync($"/api/projects/{rbl}/blocker-summary");
        Assert.Equal("rules", summary.GetProperty("generator").GetString());
        Assert.Equal("Rules-based summary", summary.GetProperty("label").GetString());
        Assert.Equal(2, summary.GetProperty("openBlockerCount").GetInt32());
        foreach (var point in summary.GetProperty("points").EnumerateArray())
        {
            Assert.True(point.GetProperty("evidence").GetArrayLength() > 0);
        }
        Assert.Contains(summary.GetProperty("points").EnumerateArray(), p => p.GetProperty("text").GetString()!.Contains("materials"));
    }

    [Fact]
    public async Task Demo_provisioning_is_rate_limited_per_client()
    {
        await using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Demo:ProvisionPermitsPerWindow", "2"));
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var http = limited.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = true });
            await http.GetAsync("/api/auth/me");
            // Read the anti-forgery cookie the same way the browser client does.
            var me = await http.GetAsync("/api/auth/me");
            var token = me.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).First(c => c.StartsWith("XSRF-TOKEN=")).Split('=', 2)[1];
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/demo/start") { Content = System.Net.Http.Json.JsonContent.Create(new { persona = "manager" }) };
            req.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(token));
            var res = await http.SendAsync(req);
            codes.Add(res.StatusCode);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Assert.Equal("rate-limited", (await res.Problem(HttpStatusCode.TooManyRequests)).Code());
                Assert.True(res.Headers.Contains("Retry-After"));
            }
        }
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests }, codes);
    }
}
