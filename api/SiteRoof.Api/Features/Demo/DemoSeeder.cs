using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SiteRoof.Api.Common;
using SiteRoof.Api.Data;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Integrations;
using SiteRoof.Api.Features.Materials;

namespace SiteRoof.Api.Features.Demo;

/// <summary>Fictional people used by the seed. Keys are stable; persona keys are what the demo switcher uses.</summary>
public static class DemoPeople
{
    public sealed record Person(string Key, string Name, string Title, string[] Roles, string? Persona);

    public static readonly Person[] All =
    [
        new("manager", "Dana Whitford", "Project Manager", [TenantRoles.ProjectManager, TenantRoles.Admin], "manager"),
        new("supervisor", "Marcus Oyelaran", "Site Supervisor", [TenantRoles.SiteSupervisor], "supervisor"),
        new("crew", "Leah Tremblay", "Drywall Foreman", [TenantRoles.CrewMember], "crew"),
        new("rosa", "Rosa Delgado", "Drywall Installer", [TenantRoles.CrewMember], null),
        new("sam", "Sam Kowalczyk", "Electrician", [TenantRoles.CrewMember], null),
        new("arjun", "Arjun Mehta", "Mechanical Fitter", [TenantRoles.CrewMember], null),
        new("tyler", "Tyler Beaulieu", "Concrete Finisher", [TenantRoles.CrewMember], null),
        new("hannah", "Hannah Lindqvist", "Glazier", [TenantRoles.CrewMember], null),
        new("grace", "Grace Okafor", "Site Supervisor", [TenantRoles.SiteSupervisor], null),
    ];

    public static readonly string[] Personas = ["manager", "supervisor", "crew"];
}

/// <summary>
/// Seeds the guided scenario into one tenant. Idempotent: each project is keyed by its code within the
/// tenant and skipped if it already exists, so re-running never duplicates or resets a workspace.
/// Dates are relative to the seed date so "overdue" stays meaningful whenever a demo is opened.
/// </summary>
public sealed class DemoSeeder(SiteRoofDbContext db, TimeProvider time)
{
    private DateTimeOffset _now;
    private DateOnly _today;
    private Guid _tenantId;
    private IReadOnlyDictionary<string, Guid> _people = new Dictionary<string, Guid>();
    private readonly Dictionary<string, Guid> _wo = [];
    private readonly Dictionary<string, Guid> _projects = [];

    public async Task<bool> SeedAsync(Guid tenantId, IReadOnlyDictionary<string, Guid> people, CancellationToken ct)
    {
        _now = time.GetUtcNow();
        _today = new SiteClock(time).Today;
        _tenantId = tenantId;
        _people = people;

        var existing = await db.Projects.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).Select(p => p.Code).ToListAsync(ct);
        var seededAny = false;
        if (!existing.Contains("RBL")) { SeedRiverbend(); seededAny = true; }
        if (!existing.Contains("NTS")) { SeedNorthgate(); seededAny = true; }
        if (!existing.Contains("MCC")) { SeedMaplewood(); seededAny = true; }
        if (!seededAny) return false;

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return true;
    }

    private DateOnly D(int days) => _today.AddDays(days);
    private DateTimeOffset T(double daysAgo, int hour = 0) => _now.AddDays(-daysAgo).AddHours(-hour);
    private Guid P(string key) => _people[key];

    // ------------------------------------------------------------------ Riverbend

    private void SeedRiverbend()
    {
        var p = NewProject("RBL", "Riverbend Library Renovation",
            "Interior renovation of a two-storey branch library: new reading room, children's area, staff offices, " +
            "fire-separation upgrades and mechanical/electrical renewal while the building stays closed to the public.",
            "Riverbend Public Library Society (fictional)", "4120 Riverbend Crescent NW", "Edmonton, AB",
            ProjectStatus.Active, D(-140), D(75), T(150));
        Members(p, ("manager", ProjectRole.Manager, "Project management"), ("supervisor", ProjectRole.Supervisor, "General"),
            ("crew", ProjectRole.Crew, "Drywall"), ("rosa", ProjectRole.Crew, "Drywall"), ("sam", ProjectRole.Crew, "Electrical"),
            ("arjun", ProjectRole.Crew, "Mechanical"));

        var m1 = NewMilestone(p, 1, "Interior demolition complete", D(-100), MilestoneStatus.Completed, T(101));
        var m2 = NewMilestone(p, 2, "Framing and MEP rough-in complete", D(-45), MilestoneStatus.Completed, T(43));
        var m3 = NewMilestone(p, 3, "Drywall and fire separations complete", D(-6), MilestoneStatus.InProgress, null);
        var m4 = NewMilestone(p, 4, "Ceilings and finishes complete", D(30), MilestoneStatus.Planned, null);
        var m5 = NewMilestone(p, 5, "Substantial completion", D(70), MilestoneStatus.Planned, null);

        Wo(p, "RBL", 1, "Interior demolition — levels 1 and 2", "Demolition", "rosa", m1, D(-102), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 1, "lot", 130,
            logs: [(1m, null, "Demolition complete, bins removed.", 103, true)]);
        Wo(p, "RBL", 2, "Steel stud framing — level 1", "Framing", "crew", m2, D(-60), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 380, "lm", 105,
            logs: [(200m, null, "East half framed.", 70, true), (180m, null, "West half framed, deflection track in.", 61, true)]);
        Wo(p, "RBL", 3, "Steel stud framing — level 2", "Framing", "rosa", m2, D(-50), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 290, "lm", 100,
            logs: [(290m, null, "Level 2 framing complete.", 51, true)]);
        Wo(p, "RBL", 4, "Electrical rough-in — level 1", "Electrical", "sam", m2, D(-48), WorkOrderPriority.High, WorkOrderStatus.Closed, 1, "lot", 98,
            logs: [(1m, null, "Rough-in inspected and passed.", 47, true)]);
        Wo(p, "RBL", 5, "HVAC ductwork rough-in", "Mechanical", "arjun", m2, D(-40), WorkOrderPriority.Normal, WorkOrderStatus.Completed, 1, "lot", 95,
            logs: [(1m, null, "Main trunk and branch ducts hung; ready for verification.", 2, false)]);
        Wo(p, "RBL", 6, "Drywall board — level 1 reading room", "Drywall", "crew", m3, D(-8), WorkOrderPriority.Critical, WorkOrderStatus.InProgress, 420, "m²", 60,
            desc: "Hang 5/8\" Type X board on reading-room walls and bulkheads. Critical path for taping, ceilings and the stair 2 fire separation.",
            logs: [(120m, null, "North and east walls boarded.", 12, true), (90m, null, "Bulkheads started.", 7, true), (50m, null, "South wall half done.", 1, false)]);
        Wo(p, "RBL", 7, "Drywall board — level 2 offices", "Drywall", "rosa", m3, D(-4), WorkOrderPriority.High, WorkOrderStatus.Blocked, 310, "m²", 55,
            desc: "Board staff offices and corridor on level 2.",
            logs: [(80m, null, "Offices 201–204 boarded.", 9, true),
                   (40m, "Short 40 sheets of 5/8\" Type X board; supplier delivery slipped to next week.", "Stopped at office 206.", 4, false)]);
        Wo(p, "RBL", 8, "Drywall taping and mudding — level 1", "Drywall", "crew", m3, D(-2), WorkOrderPriority.High, WorkOrderStatus.Open, 420, "m²", 55,
            deps: ["RBL-6"]);
        Wo(p, "RBL", 9, "Fire-rated wall inspection — stair 2", "Fire separations", null, m3, D(3), WorkOrderPriority.High, WorkOrderStatus.Open, 1, "inspection", 50,
            desc: "Book and attend the fire separation inspection once stair 2 board and firestopping are complete.", deps: ["RBL-7"]);
        Wo(p, "RBL", 10, "Firestopping at penetrations — level 2", "Fire separations", "rosa", m3, D(5), WorkOrderPriority.Normal, WorkOrderStatus.InProgress, 64, "ea", 45,
            logs: [(20m, null, "Corridor penetrations sealed.", 3, true)]);
        Wo(p, "RBL", 11, "Acoustic ceiling grid — reading room", "Ceilings", "crew", m4, D(20), WorkOrderPriority.Normal, WorkOrderStatus.Open, 380, "m²", 40,
            deps: ["RBL-8"]);
        Wo(p, "RBL", 12, "Paint — children's area", "Finishes", "rosa", m4, D(28), WorkOrderPriority.Low, WorkOrderStatus.Open, 210, "m²", 40);
        Wo(p, "RBL", 13, "Fire alarm device installation", "Electrical", "sam", m3, D(-1), WorkOrderPriority.High, WorkOrderStatus.Blocked, 46, "devices", 35,
            logs: [(18m, null, "Level 1 devices mounted.", 6, true),
                   (12m, "Ceiling access in stair 2 limited until the scaffold is moved.", "Level 2 partly done.", 2, false)]);
        Wo(p, "RBL", 14, "Circulation desk millwork install", "Millwork", null, m4, D(35), WorkOrderPriority.Normal, WorkOrderStatus.Open, 1, "lot", 30);
        Wo(p, "RBL", 15, "Mechanical commissioning — AHU-1", "Mechanical", "arjun", m5, D(55), WorkOrderPriority.Normal, WorkOrderStatus.Open, 1, "lot", 30,
            deps: ["RBL-5"]);

        Comment("RBL-7", "supervisor", "Called the supplier — next truck is Tuesday. Requested the shortfall from the yard in the meantime.", 3.5);
        Comment("RBL-7", "rosa", "Moved my crew to firestopping on level 2 until board arrives.", 3);
        Comment("RBL-6", "crew", "Need joint compound on site before taping can start on Monday.", 2);
        Comment("RBL-13", "sam", "Scaffold in stair 2 is blocking the last six heads.", 2);

        Material("RBL-7", "rosa", "5/8\" Type X gypsum board 4×8", 40, "sheets", "Delivery short-shipped; level 2 offices blocked.", 4, MaterialRequestStatus.Submitted);
        Material("RBL-6", "crew", "All-purpose joint compound", 30, "pails", "Taping starts as soon as level 1 board is up.", 2, MaterialRequestStatus.Submitted);
        Material("RBL-8", "crew", "Metal corner bead 8 ft", 120, "pcs", null, 1, MaterialRequestStatus.Submitted);
        Material("RBL-13", "sam", "Smoke detector mounting bases", 12, "ea", "Six damaged in storage; spares for level 2.", 1, MaterialRequestStatus.Submitted);
        Material("RBL-2", "crew", "362S125-33 steel studs", 200, "lm", "Additional framing at the reading-room bulkhead.", 72, MaterialRequestStatus.Approved,
            decidedDaysAgo: 71, erp: ErpSeed.Sent);
        Material("RBL-10", "rosa", "Firestop sealant 300 ml", 24, "tubes", "Level 2 penetrations.", 1, MaterialRequestStatus.Approved,
            decidedDaysAgo: 0.04, erp: ErpSeed.Retrying);

        Audit(p.Id, "manager", "Project", p.Id, AuditActions.Updated, "Updated project RBL: target date moved after drywall delay review", 5,
            new() { ["targetDate"] = D(68) }, new() { ["targetDate"] = D(75) });
        Audit(p.Id, "supervisor", "WorkOrder", _wo["RBL-7"], AuditActions.StatusChanged, "Moved RBL-007 from In progress to Blocked", 4,
            new() { ["status"] = "InProgress" }, new() { ["status"] = "Blocked" });
        Audit(p.Id, "rosa", "WorkOrder", _wo["RBL-7"], AuditActions.ProgressReported,
            "Reported 40 m² on RBL-007 with blocker: Short 40 sheets of 5/8\" Type X board", 4);
        Audit(p.Id, "manager", "MaterialRequest", Guid.Empty, AuditActions.Approved, "Approved 24 tubes Firestop sealant 300 ml for RBL-010", 0.04);
    }

    // ------------------------------------------------------------------ Northgate

    private void SeedNorthgate()
    {
        var p = NewProject("NTS", "Northgate Transit Shelter",
            "Prefabricated heated transit shelter with a new concrete pad, steel frame, tempered glazing, LED lighting and heated bench.",
            "Northgate Transit Authority (fictional)", "13750 Northgate Way NW", "Edmonton, AB",
            ProjectStatus.Active, D(-50), D(40), T(55));
        Members(p, ("manager", ProjectRole.Manager, "Project management"), ("grace", ProjectRole.Supervisor, "General"),
            ("tyler", ProjectRole.Crew, "Concrete"), ("hannah", ProjectRole.Crew, "Glazing"), ("sam", ProjectRole.Crew, "Electrical"));

        var m1 = NewMilestone(p, 1, "Foundations complete", D(-25), MilestoneStatus.Completed, T(26));
        var m2 = NewMilestone(p, 2, "Shelter structure erected", D(7), MilestoneStatus.InProgress, null);
        var m3 = NewMilestone(p, 3, "Shelter in service", D(38), MilestoneStatus.Planned, null);

        Wo(p, "NTS", 1, "Concrete pad pour and finish", "Concrete", "tyler", m1, D(-30), WorkOrderPriority.High, WorkOrderStatus.Closed, 18, "m³", 48,
            logs: [(18m, null, "Pad poured and broom finished; cylinders taken.", 31, true)]);
        Wo(p, "NTS", 2, "Anchor bolt survey and grout", "Concrete", "tyler", m1, D(-24), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 12, "ea", 45,
            logs: [(12m, null, "All anchors within tolerance.", 25, true)]);
        Wo(p, "NTS", 3, "Steel frame erection", "Structural steel", "tyler", m2, D(4), WorkOrderPriority.High, WorkOrderStatus.InProgress, 6, "frames", 30,
            logs: [(4m, null, "Four of six frames plumbed and bolted.", 1, true)]);
        Wo(p, "NTS", 4, "Tempered glazing panels", "Glazing", "hannah", m2, D(9), WorkOrderPriority.Normal, WorkOrderStatus.Open, 14, "panels", 30,
            deps: ["NTS-3"]);
        Wo(p, "NTS", 5, "Heated bench power feed", "Electrical", "sam", m3, D(15), WorkOrderPriority.Normal, WorkOrderStatus.InProgress, 1, "lot", 25,
            logs: [(0m, null, "Locates done; trenching started.", 2, false)]);
        Wo(p, "NTS", 6, "LED canopy lighting", "Electrical", "sam", m3, D(22), WorkOrderPriority.Normal, WorkOrderStatus.Open, 8, "fixtures", 25,
            deps: ["NTS-3"]);
        Wo(p, "NTS", 7, "Wayfinding signage install", "Signage", "hannah", m3, D(30), WorkOrderPriority.Low, WorkOrderStatus.Open, 5, "ea", 20);
        Wo(p, "NTS", 8, "Pedestrian detour and hoarding inspection", "Site work", "tyler", m2, D(-3), WorkOrderPriority.Normal, WorkOrderStatus.InProgress, 1, "lot", 20,
            logs: [(0m, null, "Detour signs up; waiting on transit authority walk-through.", 3, true)]);
        Wo(p, "NTS", 9, "Landscape restoration", "Site work", null, m3, D(36), WorkOrderPriority.Low, WorkOrderStatus.Open, 120, "m²", 15);

        Material("NTS-4", "hannah", "Tempered glass panel 1200×2100 (spare)", 2, "panels", "Breakage allowance for install.", 3, MaterialRequestStatus.Submitted);
        Material("NTS-2", "tyler", "Additional anchor bolts M20", 20, "ea", "Spares for frame adjustments.", 26, MaterialRequestStatus.Rejected,
            decidedDaysAgo: 25, note: "Surplus anchors are already on site from the pad pour; use those first.");

        Audit(p.Id, "grace", "WorkOrder", _wo["NTS-3"], AuditActions.ProgressReported, "Reported 4 frames on NTS-003", 1);
    }

    // ------------------------------------------------------------------ Maplewood (completed)

    private void SeedMaplewood()
    {
        var p = NewProject("MCC", "Maplewood Community Centre",
            "Envelope and interior upgrades to a community centre: roof membrane, gym windows, accessible washrooms and mechanical renewal.",
            "Maplewood Community League (fictional)", "220 Maple Crescent", "St. Albert, AB",
            ProjectStatus.Completed, D(-420), D(-60), T(430));
        Members(p, ("manager", ProjectRole.Manager, "Project management"), ("grace", ProjectRole.Supervisor, "General"),
            ("supervisor", ProjectRole.Supervisor, "General"), ("rosa", ProjectRole.Crew, "Finishes"), ("sam", ProjectRole.Crew, "Electrical"),
            ("arjun", ProjectRole.Crew, "Mechanical"));

        var m1 = NewMilestone(p, 1, "Building envelope complete", D(-200), MilestoneStatus.Completed, T(203));
        var m2 = NewMilestone(p, 2, "Interior fit-out complete", D(-90), MilestoneStatus.Completed, T(92));
        var m3 = NewMilestone(p, 3, "Occupancy and handover", D(-62), MilestoneStatus.Completed, T(61));

        Wo(p, "MCC", 1, "Roof membrane replacement", "Roofing", "rosa", m1, D(-210), WorkOrderPriority.High, WorkOrderStatus.Closed, 640, "m²", 400,
            logs: [(640m, null, "Membrane complete; warranty inspection passed.", 212, true)]);
        Wo(p, "MCC", 2, "Window replacement — gymnasium", "Glazing", "sam", m1, D(-205), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 18, "windows", 380,
            logs: [(18m, null, "All gym windows replaced.", 206, true)]);
        Wo(p, "MCC", 3, "Accessible washroom renovation", "Finishes", "rosa", m2, D(-120), WorkOrderPriority.High, WorkOrderStatus.Closed, 2, "rooms", 300,
            logs: [(2m, null, "Both washrooms complete and inspected.", 121, true)]);
        Wo(p, "MCC", 4, "LED lighting retrofit", "Electrical", "sam", m2, D(-110), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 140, "fixtures", 280,
            logs: [(140m, null, "Retrofit complete.", 112, true)]);
        Wo(p, "MCC", 5, "Rooftop unit replacement", "Mechanical", "arjun", m2, D(-100), WorkOrderPriority.High, WorkOrderStatus.Closed, 2, "units", 260,
            logs: [(2m, null, "RTU-1 and RTU-2 commissioned.", 101, true)]);
        Wo(p, "MCC", 6, "Gym floor refinishing", "Finishes", "rosa", m3, D(-70), WorkOrderPriority.Normal, WorkOrderStatus.Closed, 520, "m²", 120,
            logs: [(520m, null, "Two coats applied; cured.", 71, true)]);

        Material("MCC-3", "rosa", "Stainless grab bars 36 in", 6, "ea", "Accessible washrooms.", 130, MaterialRequestStatus.Approved,
            decidedDaysAgo: 129, erp: ErpSeed.Sent);
    }

    // ------------------------------------------------------------------ builders

    private enum ErpSeed { None, Sent, Retrying }

    private Project NewProject(string code, string title, string description, string client, string address, string city,
        ProjectStatus status, DateOnly start, DateOnly target, DateTimeOffset created)
    {
        var p = new Project
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, Code = code, Title = title, Description = description, ClientName = client,
            SiteAddress = address, City = city, Status = status, StartDate = start, TargetDate = target, CreatedAt = created,
        };
        db.Projects.Add(p);
        _projects[code] = p.Id;
        Audit(p.Id, "manager", "Project", p.Id, AuditActions.Created, $"Created project {code} · {title}", (_now - created).TotalDays);
        return p;
    }

    private void Members(Project p, params (string Person, ProjectRole Role, string Trade)[] members)
    {
        foreach (var (person, role, trade) in members)
        {
            db.ProjectMemberships.Add(new ProjectMembership { ProjectId = p.Id, UserId = P(person), Role = role, Trade = trade, AddedAt = p.CreatedAt });
        }
    }

    private Milestone NewMilestone(Project p, int sort, string name, DateOnly due, MilestoneStatus status, DateTimeOffset? completed)
    {
        var m = new Milestone { Id = Guid.NewGuid(), ProjectId = p.Id, Name = name, DueDate = due, Status = status, CompletedAt = completed, SortOrder = sort };
        db.Milestones.Add(m);
        return m;
    }

    private void Wo(Project p, string code, int number, string title, string package, string? assignee, Milestone milestone, DateOnly due,
        WorkOrderPriority priority, WorkOrderStatus status, decimal planned, string unit, double createdDaysAgo,
        string? desc = null, (decimal Qty, string? Blocker, string? Note, double DaysAgo, bool Verified)[]? logs = null, string[]? deps = null)
    {
        var created = T(createdDaysAgo);
        var supervisor = code == "NTS" ? "grace" : "supervisor";
        var wo = new WorkOrder
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = p.Id, Number = number, Title = title, Description = desc, WorkPackage = package,
            AssigneeId = assignee is null ? null : P(assignee), MilestoneId = milestone.Id, DueDate = due, Priority = priority, Status = status,
            PlannedQuantity = planned, Unit = unit, CreatedById = P(supervisor), CreatedAt = created, UpdatedAt = created,
        };
        var lastLog = logs?.Length > 0 ? T(logs.Min(l => l.DaysAgo)) : created;
        if (status is WorkOrderStatus.Completed or WorkOrderStatus.Closed) wo.CompletedAt = lastLog;
        if (status == WorkOrderStatus.Closed) wo.ClosedAt = lastLog.AddHours(20);
        wo.UpdatedAt = wo.ClosedAt ?? lastLog;
        db.WorkOrders.Add(wo);
        _wo[$"{code}-{number}"] = wo.Id;

        foreach (var (qty, blocker, note, daysAgo, verified) in logs ?? [])
        {
            db.ProgressLogs.Add(new ProgressLog
            {
                Id = Guid.NewGuid(), WorkOrderId = wo.Id, ReporterId = assignee is null ? P(supervisor) : P(assignee), Quantity = qty, Blocker = blocker,
                Note = note, CreatedAt = T(daysAgo, 3),
                VerifiedById = verified ? P(supervisor) : null, VerifiedAt = verified ? T(daysAgo - 0.2) : null,
            });
        }
        foreach (var dep in deps ?? [])
        {
            db.WorkOrderDependencies.Add(new WorkOrderDependency { WorkOrderId = wo.Id, DependsOnWorkOrderId = _wo[dep] });
        }
        Audit(p.Id, supervisor, "WorkOrder", wo.Id, AuditActions.Created, $"Created {code}-{number:000} · {title}", createdDaysAgo);
    }

    private void Comment(string wo, string author, string body, double daysAgo) =>
        db.WorkOrderComments.Add(new WorkOrderComment { Id = Guid.NewGuid(), WorkOrderId = _wo[wo], AuthorId = P(author), Body = body, CreatedAt = T(daysAgo) });

    private void Material(string wo, string requester, string item, decimal qty, string unit, string? justification, double daysAgo,
        MaterialRequestStatus status, double decidedDaysAgo = 0, string? note = null, ErpSeed erp = ErpSeed.None)
    {
        var code = wo.Split('-')[0];
        var number = int.Parse(wo.Split('-')[1]);
        var mr = new MaterialRequest
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, WorkOrderId = _wo[wo], ItemName = item, Quantity = qty, Unit = unit, Justification = justification,
            Status = status, RequestedById = P(requester), RequestedAt = T(daysAgo, 2), ClientRequestId = Guid.NewGuid(),
        };
        if (status != MaterialRequestStatus.Submitted)
        {
            mr.DecidedById = P("manager");
            mr.DecidedAt = T(decidedDaysAgo);
            mr.DecisionNote = note;
        }
        db.MaterialRequests.Add(mr);
        var woKey = $"{code}-{number:000}";
        Audit(_projects[code], requester, "MaterialRequest", mr.Id, AuditActions.Submitted, $"Requested {qty:0.##} {unit} {item} for {woKey}", daysAgo);
        if (status == MaterialRequestStatus.Rejected)
        {
            Audit(_projects[code], "manager", "MaterialRequest", mr.Id, AuditActions.Rejected, $"Rejected {qty:0.##} {unit} {item} for {woKey} — {note}",
                decidedDaysAgo, new() { ["status"] = "Submitted" }, new() { ["status"] = "Rejected", ["note"] = note });
        }

        if (erp == ErpSeed.None) return;
        var key = OutboxKeys.ForApproval(mr.Id);
        var payload = JsonSerializer.Serialize(new RequisitionPayload(key, _tenantId, mr.Id, code, woKey, item, qty, unit,
            DemoPeople.All.Single(x => x.Key == requester).Name, "Dana Whitford", mr.DecidedAt!.Value), AuditWriter.JsonOptions);
        var outbox = new IntegrationOutbox
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, EventType = MaterialRequestService.ApprovedEventType, AggregateId = mr.Id, Payload = payload,
            IdempotencyKey = key, CreatedAt = mr.DecidedAt.Value,
        };
        if (erp == ErpSeed.Sent)
        {
            var reference = $"PR-{mr.DecidedAt.Value:yyMMdd}-{mr.Id.ToString("N")[..6].ToUpperInvariant()}";
            outbox.Status = OutboxStatus.Succeeded;
            outbox.Attempts = 1;
            outbox.NextRetryAt = mr.DecidedAt.Value;
            outbox.ProcessedAt = mr.DecidedAt.Value.AddSeconds(3);
            outbox.ExternalReference = reference;
            mr.ExternalReference = reference;
            db.MockErpRequisitions.Add(new MockErpRequisition
            {
                Id = Guid.NewGuid(), IdempotencyKey = key, ExternalReference = reference, TenantId = _tenantId, Payload = payload, ReceivedAt = outbox.ProcessedAt.Value,
            });
            db.MockErpCalls.Add(new MockErpCall { IdempotencyKey = key, TenantId = _tenantId, ResponseStatus = 200, ReceivedAt = outbox.ProcessedAt.Value });
            Audit(_projects[code], "manager", "MaterialRequest", mr.Id, AuditActions.Approved, $"Approved {qty:0.##} {unit} {item} for {woKey}", decidedDaysAgo,
                new() { ["status"] = "Submitted" }, new() { ["status"] = "Approved" });
            Audit(_projects[code], null, "MaterialRequest", mr.Id, AuditActions.ErpAcknowledged,
                $"ERP acknowledged requisition for {qty:0.##} {unit} {item} as {reference} (attempt 1)", decidedDaysAgo - 0.001);
        }
        else
        {
            // One attempt already failed with a simulated 503; the dispatcher retries it shortly after the demo opens.
            outbox.Status = OutboxStatus.Pending;
            outbox.Attempts = 1;
            outbox.LastError = "503 Service Unavailable";
            outbox.NextRetryAt = _now.AddSeconds(75);
            db.MockErpCalls.Add(new MockErpCall { IdempotencyKey = key, TenantId = _tenantId, ResponseStatus = 503, ReceivedAt = mr.DecidedAt.Value.AddSeconds(2) });
            Audit(_projects[code], null, "MaterialRequest", mr.Id, AuditActions.ErpRetryScheduled,
                $"ERP returned 503 Service Unavailable for {qty:0.##} {unit} {item}; retry 2 of 5 scheduled", decidedDaysAgo - 0.0005,
                after: new() { ["attempts"] = 1, ["error"] = "503 Service Unavailable" });
        }
        db.IntegrationOutbox.Add(outbox);
        _lastApproval = mr.Id;
    }

    private Guid _lastApproval;

    private void Audit(Guid projectId, string? actor, string entityType, Guid entityId, string action, string summary, double daysAgo,
        Dictionary<string, object?>? before = null, Dictionary<string, object?>? after = null)
    {
        if (entityId == Guid.Empty) entityId = _lastApproval;
        db.AuditEvents.Add(new AuditEvent
        {
            TenantId = _tenantId, ProjectId = projectId, ActorId = actor is null ? null : P(actor), EntityType = entityType, EntityId = entityId,
            Action = action, Summary = summary,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, AuditWriter.JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, AuditWriter.JsonOptions),
            CreatedAt = T(Math.Max(0, daysAgo)),
        });
    }
}
