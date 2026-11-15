using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Auth;
using SiteRoof.Api.Infrastructure;

namespace SiteRoof.Api.Features.Demo;

public sealed class PersonaRequest
{
    [Required, RegularExpression("^(manager|supervisor|crew)$", ErrorMessage = "Choose manager, supervisor or crew.")]
    public string Persona { get; set; } = "manager";
}

public sealed record PersonaDto(string Key, string DisplayName, string JobTitle, string RoleLabel, string Summary, bool Active);

public sealed record ScenarioStepDto(int Order, string Title, string Description, string Persona, string Link, bool? Done);

public sealed record ScenarioDto(
    Guid TenantId,
    string TenantName,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<PersonaDto> Personas,
    IReadOnlyList<ScenarioStepDto> Steps,
    Guid? RiverbendProjectId);

[ApiController]
[Route("api/demo")]
[Produces("application/json")]
public sealed class DemoController(
    SiteRoofDbContext db,
    DemoProvisioner provisioner,
    TenantDeleter deleter,
    SessionService sessions,
    ICurrentUser user) : ControllerBase
{
    private static readonly Dictionary<string, (string Role, string Summary)> PersonaInfo = new()
    {
        ["manager"] = ("Project Manager", "Approves material requests, reviews progress across all three projects."),
        ["supervisor"] = ("Site Supervisor", "Creates and assigns work orders, verifies progress on Riverbend and Maplewood."),
        ["crew"] = ("Crew Member", "Sees only Riverbend; reports progress and blockers on assigned drywall work."),
    };

    /// <summary>
    /// Provision a fresh, isolated demo workspace and sign in as the chosen persona. Rate-limited per IP.
    /// If the caller is already in an unexpired demo workspace, that session is returned instead.
    /// </summary>
    [HttpPost("start")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.DemoProvision)]
    public async Task<SessionDto> Start([FromBody] PersonaRequest? req, CancellationToken ct)
    {
        var persona = req?.Persona ?? "manager";
        if (user.IsAuthenticated && user.TenantId is { } current
            && await db.Tenants.AnyAsync(t => t.Id == current && t.IsDemo && t.ExpiresAt > DateTimeOffset.UtcNow, ct))
        {
            var session = await sessions.CurrentAsync();
            if (session.Authenticated) return session;
        }
        var tenant = await provisioner.ProvisionAsync(ct);
        return await sessions.SignInAsync(await PersonaUserAsync(tenant.Id, persona, ct));
    }

    /// <summary>Guided scenario for the current demo workspace, with completion computed from stored records.</summary>
    [HttpGet("scenario")]
    [Authorize]
    public async Task<ScenarioDto> Scenario(CancellationToken ct)
    {
        var tenant = await CurrentDemoTenantAsync(ct);
        var people = await db.Users.AsNoTracking().Where(u => u.TenantId == tenant.Id && u.PersonaKey != null).ToListAsync(ct);
        var riverbend = await db.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenant.Id && p.Code == "RBL").Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct);
        var since = tenant.CreatedAt;
        var crewId = people.FirstOrDefault(p => p.PersonaKey == "crew")?.Id;

        var progressByCrew = await db.ProgressLogs.IgnoreQueryFilters().AnyAsync(p => p.WorkOrder!.TenantId == tenant.Id && p.CreatedAt > since && p.ReporterId == crewId, ct);
        var verified = await db.ProgressLogs.IgnoreQueryFilters().AnyAsync(p => p.WorkOrder!.TenantId == tenant.Id && p.VerifiedAt > since, ct);
        var decided = await db.MaterialRequests.IgnoreQueryFilters().AnyAsync(m => m.TenantId == tenant.Id && m.DecidedAt > since, ct);
        var erpSent = await db.IntegrationOutbox.IgnoreQueryFilters().AnyAsync(o => o.TenantId == tenant.Id && o.Status == OutboxStatus.Succeeded && o.ProcessedAt > since, ct);
        var rbl = riverbend is null ? "/app/projects" : $"/app/projects/{riverbend}";

        var steps = new List<ScenarioStepDto>
        {
            new(1, "Open Riverbend and inspect delayed work",
                "Riverbend Library Renovation is behind on drywall. Filter the board to overdue work and open RBL-007 to see the material shortage.",
                "supervisor", rbl + "?overdue=true", null),
            new(2, "Submit progress as the crew",
                "Switch to Leah (crew). On RBL-006 report square metres installed, and optionally a blocker.", "crew", rbl, progressByCrew),
            new(3, "Verify the update",
                "Switch back to Marcus (supervisor) and verify Leah's progress log.", "supervisor", rbl, verified),
            new(4, "Approve a material request",
                "As Dana (manager), open Approvals and approve the 40 sheets of Type X board for RBL-007.", "manager", "/app/approvals", decided),
            new(5, "Watch the ERP transmission",
                "The approval writes an outbox event in the same transaction. The mock ERP rejects the first attempt with 503; the worker retries with backoff.",
                "manager", "/app/approvals", erpSent),
            new(6, "See the dashboard update",
                "Home and the project dashboard are recalculated from the database on every load.", "manager", "/app/home", null),
        };

        var personas = DemoPeople.Personas.Select(key =>
        {
            var u = people.Single(p => p.PersonaKey == key);
            return new PersonaDto(key, u.DisplayName, u.JobTitle ?? "", PersonaInfo[key].Role, PersonaInfo[key].Summary, u.Id == user.UserId);
        }).ToList();

        return new ScenarioDto(tenant.Id, tenant.Name, tenant.ExpiresAt, personas, steps, riverbend);
    }

    /// <summary>Switch to another persona of the same demo workspace (never another visitor's).</summary>
    [HttpPost("persona")]
    [Authorize]
    public async Task<SessionDto> Persona(PersonaRequest req, CancellationToken ct)
    {
        var tenant = await CurrentDemoTenantAsync(ct);
        return await sessions.SignInAsync(await PersonaUserAsync(tenant.Id, req.Persona, ct));
    }

    /// <summary>Replace the current demo workspace with a freshly seeded one and delete the old one.</summary>
    [HttpPost("reset")]
    [Authorize]
    [EnableRateLimiting(RateLimits.DemoProvision)]
    public async Task<SessionDto> Reset(CancellationToken ct)
    {
        var old = await CurrentDemoTenantAsync(ct);
        var persona = await db.Users.Where(u => u.Id == user.UserId).Select(u => u.PersonaKey).SingleOrDefaultAsync(ct) ?? "manager";
        var fresh = await provisioner.ProvisionAsync(ct);
        var session = await sessions.SignInAsync(await PersonaUserAsync(fresh.Id, persona, ct));
        await deleter.DeleteAsync(old.Id, ct);
        return session;
    }

    private async Task<Tenant> CurrentDemoTenantAsync(CancellationToken ct)
    {
        var tenantId = user.RequireTenantId();
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, ct) ?? throw new NotFoundException("Workspace not found.");
        if (!tenant.IsDemo) throw new ForbiddenException("Demo controls are only available in demo workspaces.");
        return tenant;
    }

    private async Task<AppUser> PersonaUserAsync(Guid tenantId, string persona, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(u => u.TenantId == tenantId && u.PersonaKey == persona, ct)
        ?? throw new NotFoundException("Persona not found in this workspace.");
}
