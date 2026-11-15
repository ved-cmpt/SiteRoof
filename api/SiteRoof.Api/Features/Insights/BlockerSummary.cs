using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Auth;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Dashboard;
using SiteRoof.Api.Features.WorkOrders;

namespace SiteRoof.Api.Features.Insights;

public sealed record EvidenceDto(string Type, Guid Id, string Label);

public sealed record SummaryPointDto(string Severity, string Text, IReadOnlyList<EvidenceDto> Evidence);

public sealed record BlockerSummaryDto(
    string Generator,
    string Label,
    string Headline,
    IReadOnlyList<SummaryPointDto> Points,
    IReadOnlyList<string> SuggestedActions,
    int OpenBlockerCount,
    DateTimeOffset GeneratedAt,
    string Disclaimer);

/// <summary>Everything a summary may cite. Providers only see this minimal, already-authorized context.</summary>
public sealed record BlockerContext(
    string ProjectCode,
    DateOnly Today,
    IReadOnlyList<BlockerDto> Blockers,
    IReadOnlyList<BlockedWork> Work,
    IReadOnlyList<PendingMaterial> PendingMaterials);

public sealed record BlockedWork(Guid Id, string Key, string Title, WorkOrderStatus Status, DateOnly DueDate, string? Milestone,
    DateOnly? MilestoneDue, IReadOnlyList<string> DependentKeys);

public sealed record PendingMaterial(Guid Id, Guid WorkOrderId, string WorkOrderKey, string Description, int AgeDays);

/// <summary>
/// Extension point for summary generators. The default is deterministic and rules-based. An AI-backed
/// provider can be registered later (see docs/ARCHITECTURE.md); it is disabled by default, receives only
/// <see cref="BlockerContext"/>, and its evidence is filtered against that context before display. It may
/// summarize; it never approves or changes anything.
/// </summary>
public interface IBlockerSummaryProvider
{
    string Name { get; }
    bool IsEnabled { get; }
    Task<BlockerSummaryDto> SummarizeAsync(BlockerContext context, CancellationToken ct);
}

public sealed class InsightsOptions
{
    public const string Section = "Insights";

    /// <summary>Name of an optional AI provider. Null/empty (default) uses the rules-based summary only.</summary>
    public string? AiProvider { get; set; }
}

public sealed class RulesBasedBlockerSummaryProvider(TimeProvider time) : IBlockerSummaryProvider
{
    public const string GeneratorName = "rules";
    public const string LabelText = "Rules-based summary";

    public string Name => GeneratorName;
    public bool IsEnabled => true;

    private static readonly (string Category, string[] Keywords)[] Categories =
    [
        ("materials", ["material", "short", "shortage", "missing", "delivery", "deliver", "backorder", "supplier", "sheets", "stock", "pallet"]),
        ("inspection", ["inspection", "inspector", "permit", "approval from city", "sign-off", "signoff"]),
        ("design", ["rfi", "drawing", "design", "clarification", "consultant", "engineer"]),
        ("access", ["access", "scaffold", "lift", "power", "occupied", "keys", "closure"]),
        ("labour", ["crew", "labour", "labor", "sick", "manpower", "trade"]),
        ("weather", ["weather", "rain", "snow", "cold", "wind", "frost"]),
    ];

    public static string Classify(string text)
    {
        var t = text.ToLowerInvariant();
        foreach (var (category, keywords) in Categories)
        {
            if (keywords.Any(k => t.Contains(k))) return category;
        }
        return "other";
    }

    public Task<BlockerSummaryDto> SummarizeAsync(BlockerContext c, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var points = new List<SummaryPointDto>();
        var actions = new List<string>();
        var workById = c.Work.ToDictionary(w => w.Id);

        if (c.Blockers.Count == 0)
        {
            return Task.FromResult(new BlockerSummaryDto(GeneratorName, LabelText, $"No open blockers on {c.ProjectCode}.", [], [],
                0, now, Disclaimer));
        }

        EvidenceDto Wo(Guid id, string key) => new("workOrder", id, key);

        // 1. Blockers grouped by cause category, largest group first.
        foreach (var group in c.Blockers.GroupBy(b => Classify(b.Blocker))
                     .OrderByDescending(g => g.Count()).ThenBy(g => g.Key == "materials" ? 0 : 1).ThenBy(g => g.Key))
        {
            var keys = group.Select(b => b.WorkOrderKey).Distinct().ToList();
            var text = $"{group.Count()} open blocker{Plural(group.Count())} {CategoryPhrase(group.Key, group.Count() == 1)} on {Join(keys)}.";
            var withPending = group.Key == "materials"
                ? c.PendingMaterials.Where(m => group.Any(b => b.WorkOrderId == m.WorkOrderId)).ToList()
                : [];
            if (withPending.Count > 0)
            {
                text += $" {withPending.Count} related material request{Plural(withPending.Count)} {(withPending.Count == 1 ? "is" : "are")} awaiting approval.";
            }
            points.Add(new SummaryPointDto(group.Key == "materials" || group.Count() > 1 ? "warning" : "info", text,
                group.Select(b => new EvidenceDto("progressLog", b.ProgressLogId, $"{b.WorkOrderKey}: {Short(b.Blocker)}"))
                    .Concat(withPending.Select(m => new EvidenceDto("materialRequest", m.Id, m.Description))).ToList()));
        }

        // 2. Oldest blocker.
        var oldest = c.Blockers.MaxBy(b => b.AgeDays)!;
        if (oldest.AgeDays >= 1)
        {
            points.Add(new SummaryPointDto(oldest.AgeDays >= 5 ? "critical" : "warning",
                $"Oldest blocker has been open {oldest.AgeDays} day{Plural(oldest.AgeDays)} on {oldest.WorkOrderKey} (reported by {oldest.ReportedBy}).",
                [Wo(oldest.WorkOrderId, oldest.WorkOrderKey)]));
            if (oldest.AgeDays >= 5) actions.Add($"Escalate {oldest.WorkOrderKey}: its blocker is older than five days.");
        }

        // 3. Blocked work that is already past due.
        var blockedIds = c.Blockers.Select(b => b.WorkOrderId).ToHashSet();
        var lateBlocked = c.Work.Where(w => blockedIds.Contains(w.Id) && w.DueDate < c.Today).OrderBy(w => w.DueDate).ToList();
        if (lateBlocked.Count > 0)
        {
            points.Add(new SummaryPointDto("critical",
                $"{lateBlocked.Count} blocked work order{Plural(lateBlocked.Count)} {(lateBlocked.Count == 1 ? "is" : "are")} past due: " +
                string.Join(", ", lateBlocked.Select(w => $"{w.Key} (due {w.DueDate:MMM d})")) + ".",
                lateBlocked.Select(w => Wo(w.Id, w.Key)).ToList()));
        }

        // 4. Downstream impact through dependencies.
        var downstream = c.Work.Where(w => blockedIds.Contains(w.Id) && w.DependentKeys.Count > 0).ToList();
        if (downstream.Count > 0)
        {
            var dependents = downstream.SelectMany(w => w.DependentKeys).Distinct().Order().ToList();
            points.Add(new SummaryPointDto("warning",
                $"{Join(dependents)} {(dependents.Count == 1 ? "is" : "are")} waiting on blocked work ({Join(downstream.Select(w => w.Key).ToList())}).",
                downstream.Select(w => Wo(w.Id, w.Key)).ToList()));
        }

        // 5. Milestone exposure (milestone overdue or due within 14 days).
        var milestoneRisk = c.Work.Where(w => blockedIds.Contains(w.Id) && w.MilestoneDue is { } due && due.DayNumber - c.Today.DayNumber <= 14)
            .GroupBy(w => (w.Milestone!, w.MilestoneDue!.Value)).ToList();
        foreach (var g in milestoneRisk)
        {
            var days = g.Key.Value.DayNumber - c.Today.DayNumber;
            points.Add(new SummaryPointDto(days < 0 ? "critical" : "warning",
                $"Milestone \"{g.Key.Item1}\" {(days < 0 ? $"is {-days} day{Plural(-days)} overdue" : $"is due in {days} day{Plural(days)}")} with blocked work: {Join(g.Select(w => w.Key).ToList())}.",
                g.Select(w => Wo(w.Id, w.Key)).ToList()));
        }

        // Suggested actions are recommendations for people; nothing is approved automatically.
        var pendingForBlocked = c.PendingMaterials.Where(m => blockedIds.Contains(m.WorkOrderId)).ToList();
        if (pendingForBlocked.Count > 0)
        {
            actions.Insert(0, $"Review {pendingForBlocked.Count} pending material request{Plural(pendingForBlocked.Count)} tied to blocked work ({Join(pendingForBlocked.Select(m => m.WorkOrderKey).Distinct().ToList())}).");
        }
        if (c.Blockers.Any(b => Classify(b.Blocker) == "inspection")) actions.Add("Confirm inspection bookings with the authority having jurisdiction.");
        if (c.Blockers.Any(b => Classify(b.Blocker) == "design")) actions.Add("Chase open RFIs with the design consultant.");

        var critical = points.Count(p => p.Severity == "critical");
        var headline = $"{c.Blockers.Count} open blocker{Plural(c.Blockers.Count)} across {blockedIds.Count} work order{Plural(blockedIds.Count)} on {c.ProjectCode}" +
                       (critical > 0 ? $"; {critical} item{Plural(critical)} need{(critical == 1 ? "s" : "")} attention now." : ".");

        return Task.FromResult(new BlockerSummaryDto(GeneratorName, LabelText, headline, points, actions, c.Blockers.Count, now, Disclaimer));
    }

    private const string Disclaimer = "Generated by fixed rules from this project's records. Every statement links to its evidence; no AI model is used.";

    private static string CategoryPhrase(string category, bool singular) => (category, singular) switch
    {
        ("materials", true) => "relates to materials or deliveries",
        ("materials", false) => "relate to materials or deliveries",
        ("inspection", true) => "waits on an inspection or permit",
        ("inspection", false) => "wait on inspections or permits",
        ("design", true) => "waits on a design clarification",
        ("design", false) => "wait on design clarifications",
        ("access", true) => "involves site access or temporary works",
        ("access", false) => "involve site access or temporary works",
        ("labour", true) => "involves crew availability",
        ("labour", false) => "involve crew availability",
        ("weather", true) => "is weather related",
        ("weather", false) => "are weather related",
        (_, true) => "is uncategorized",
        _ => "are uncategorized",
    };

    private static string Plural(int n) => n == 1 ? "" : "s";

    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    private static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
}

public sealed class BlockerSummaryService(
    SiteRoofDbContext db,
    ProjectAccess access,
    DashboardService dashboards,
    SiteClock clock,
    IEnumerable<IBlockerSummaryProvider> providers,
    Microsoft.Extensions.Options.IOptions<InsightsOptions> options,
    ILogger<BlockerSummaryService> logger)
{
    public async Task<BlockerSummaryDto> ForProjectAsync(Guid projectId, CancellationToken ct)
    {
        await access.ForProjectAsync(projectId, ct);
        var code = await db.Projects.Where(p => p.Id == projectId).Select(p => p.Code).SingleAsync(ct);
        var blockers = await dashboards.OpenBlockersAsync(projectId, code, ct);
        var woIds = blockers.Select(b => b.WorkOrderId).Distinct().ToList();

        var work = await db.WorkOrders.AsNoTracking().Where(w => woIds.Contains(w.Id))
            .Select(w => new
            {
                w.Id, w.Number, w.Title, w.Status, w.DueDate,
                Milestone = w.Milestone != null ? w.Milestone.Name : null,
                MilestoneDue = w.Milestone != null && w.Milestone.Status != MilestoneStatus.Completed ? (DateOnly?)w.Milestone.DueDate : null,
            }).ToListAsync(ct);
        var dependents = await db.WorkOrderDependencies.AsNoTracking()
            .Where(d => woIds.Contains(d.DependsOnWorkOrderId)
                && d.WorkOrder!.Status != WorkOrderStatus.Closed && d.WorkOrder.Status != WorkOrderStatus.Cancelled)
            .Select(d => new { d.DependsOnWorkOrderId, d.WorkOrder!.Number }).ToListAsync(ct);
        var now = clock.Now;
        var pending = await db.MaterialRequests.AsNoTracking()
            .Where(m => m.WorkOrder!.ProjectId == projectId && m.Status == MaterialRequestStatus.Submitted)
            .Select(m => new { m.Id, m.WorkOrderId, m.WorkOrder!.Number, m.Quantity, m.Unit, m.ItemName, m.RequestedAt }).ToListAsync(ct);

        var context = new BlockerContext(code, clock.Today, blockers,
            work.Select(w => new BlockedWork(w.Id, WorkOrderService.Key(code, w.Number), w.Title, w.Status, w.DueDate, w.Milestone, w.MilestoneDue,
                dependents.Where(d => d.DependsOnWorkOrderId == w.Id).Select(d => WorkOrderService.Key(code, d.Number)).ToList())).ToList(),
            pending.Select(m => new PendingMaterial(m.Id, m.WorkOrderId, WorkOrderService.Key(code, m.Number), $"{m.Quantity:0.##} {m.Unit} {m.ItemName}",
                DashboardService.AgeDays(now, m.RequestedAt))).ToList());

        var rules = providers.First(p => p.Name == RulesBasedBlockerSummaryProvider.GeneratorName);
        var preferred = string.IsNullOrWhiteSpace(options.Value.AiProvider)
            ? null
            : providers.FirstOrDefault(p => p.Name == options.Value.AiProvider && p.IsEnabled);
        if (preferred is not null)
        {
            try
            {
                return FilterEvidence(await preferred.SummarizeAsync(context, ct), context);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Optional summary provider {Provider} failed; falling back to rules", preferred.Name);
            }
        }
        return await rules.SummarizeAsync(context, ct);
    }

    /// <summary>Drops any evidence that is not part of the authorized context (defence against fabricated links).</summary>
    public static BlockerSummaryDto FilterEvidence(BlockerSummaryDto summary, BlockerContext context)
    {
        var allowed = context.Blockers.Select(b => b.ProgressLogId)
            .Concat(context.Blockers.Select(b => b.WorkOrderId))
            .Concat(context.Work.Select(w => w.Id))
            .Concat(context.PendingMaterials.Select(m => m.Id)).ToHashSet();
        return summary with
        {
            Points = summary.Points
                .Select(p => p with { Evidence = p.Evidence.Where(e => allowed.Contains(e.Id)).ToList() })
                .Where(p => p.Evidence.Count > 0)
                .ToList(),
        };
    }
}

[ApiController]
[Authorize]
[Route("api")]
[Produces("application/json")]
public sealed class InsightsController(BlockerSummaryService summaries) : ControllerBase
{
    /// <summary>Evidence-linked summary of a project's open blockers. Rules-based unless an optional provider is configured.</summary>
    [HttpGet("projects/{id:guid}/blocker-summary")]
    public Task<BlockerSummaryDto> BlockerSummary(Guid id, CancellationToken ct) => summaries.ForProjectAsync(id, ct);
}
