using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    public bool Enabled { get; set; } = true;
    public double LifetimeHours { get; set; } = 8;
    public int MaxActiveTenants { get; set; } = 300;
    public double CleanupIntervalMinutes { get; set; } = 10;
    public int ProvisionPermitsPerWindow { get; set; } = 6;
    public double ProvisionWindowMinutes { get; set; } = 15;
}

/// <summary>
/// Creates one isolated guest tenant per visitor: its own users (no passwords), roles, memberships and a
/// freshly seeded scenario. Nothing is shared between visitors, so one recruiter's clicks never change another's data.
/// </summary>
public sealed class DemoProvisioner(
    SiteRoofDbContext db,
    UserManager<AppUser> users,
    DemoSeeder seeder,
    TenantDeleter deleter,
    TimeProvider time,
    IOptions<DemoOptions> options,
    ILogger<DemoProvisioner> logger)
{
    public async Task<Tenant> ProvisionAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled) throw new ForbiddenException("The guest demo is disabled on this server.");
        var now = time.GetUtcNow();
        var active = await db.Tenants.CountAsync(t => t.IsDemo && t.ExpiresAt > now, ct);
        if (active >= opt.MaxActiveTenants)
        {
            throw new BusinessRuleException("demo-capacity", "The demo is at capacity right now. Please try again in a few minutes.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Aspen Ridge Contracting (demo)",
            IsDemo = true,
            ErpSimulatorMode = ErpSimulatorMode.FailFirstAttempt,
            CreatedAt = now,
            ExpiresAt = now.AddHours(opt.LifetimeHours),
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        try
        {
            var shortId = tenant.Id.ToString("N")[..10];
            var people = new Dictionary<string, Guid>();
            foreach (var person in DemoPeople.All)
            {
                var email = $"{person.Key}.{shortId}@demo.siteroof.invalid";
                var user = new AppUser
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DisplayName = person.Name,
                    JobTitle = person.Title,
                    PersonaKey = person.Persona,
                    CreatedAt = now,
                    // No password: demo personas can only be entered through the demo session flow.
                    SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                };
                Check(await users.CreateAsync(user));
                Check(await users.AddToRolesAsync(user, person.Roles));
                people[person.Key] = user.Id;
            }

            await seeder.SeedAsync(tenant.Id, people, ct);
            logger.LogInformation("Provisioned demo tenant {TenantId} expiring {ExpiresAt}", tenant.Id, tenant.ExpiresAt);
            return tenant;
        }
        catch
        {
            db.ChangeTracker.Clear();
            await deleter.DeleteAsync(tenant.Id, CancellationToken.None);
            throw;
        }
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Demo user creation failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}

/// <summary>Deletes every row that belongs to one tenant, children first, in a single transaction.</summary>
public sealed class TenantDeleter(SiteRoofDbContext db, ILogger<TenantDeleter> logger)
{
    public async Task DeleteAsync(Guid tenantId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.AuditEvents.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.IntegrationOutbox.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.MockErpCalls.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.MockErpRequisitions.Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.MaterialRequests.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.ProgressLogs.IgnoreQueryFilters().Where(x => x.WorkOrder!.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.WorkOrderComments.IgnoreQueryFilters().Where(x => x.WorkOrder!.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.WorkOrderDependencies.IgnoreQueryFilters().Where(x => x.WorkOrder!.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.WorkOrders.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.Milestones.IgnoreQueryFilters().Where(x => x.Project!.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.ProjectMemberships.IgnoreQueryFilters().Where(x => x.Project!.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.Projects.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);

            var userIds = db.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id);
            await db.UserRoles.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
            await db.UserClaims.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
            await db.UserLogins.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
            await db.UserTokens.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync(ct);
            await db.Users.Where(u => u.TenantId == tenantId).ExecuteDeleteAsync(ct);
            await db.Tenants.Where(t => t.Id == tenantId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });
        logger.LogInformation("Deleted tenant {TenantId}", tenantId);
    }
}

/// <summary>Deletes expired guest tenants on a schedule.</summary>
public sealed class DemoCleanupService(IServiceScopeFactory scopes, IOptions<DemoOptions> options, TimeProvider time, ILogger<DemoCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(0.1, options.Value.CleanupIntervalMinutes)));
        do
        {
            try
            {
                var removed = await RunOnceAsync(scopes, time, stoppingToken);
                if (removed > 0) logger.LogInformation("Demo cleanup removed {Count} expired demo tenants", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Demo cleanup run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public static async Task<int> RunOnceAsync(IServiceScopeFactory scopes, TimeProvider time, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SiteRoofDbContext>();
        var deleter = scope.ServiceProvider.GetRequiredService<TenantDeleter>();
        var now = time.GetUtcNow();
        var expired = await db.Tenants.Where(t => t.IsDemo && t.ExpiresAt < now).OrderBy(t => t.ExpiresAt).Select(t => t.Id).Take(25).ToListAsync(ct);
        foreach (var id in expired) await deleter.DeleteAsync(id, ct);
        return expired.Count;
    }
}
