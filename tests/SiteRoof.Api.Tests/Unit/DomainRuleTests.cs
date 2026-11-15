using SiteRoof.Api.Common;
using SiteRoof.Api.Domain;
using SiteRoof.Api.Features.Dashboard;
using SiteRoof.Api.Features.Insights;
using SiteRoof.Api.Features.Integrations;
using SiteRoof.Api.Features.WorkOrders;

namespace SiteRoof.Api.Tests.Unit;

public sealed class WorkOrderStateMachineTests
{
    private static readonly WorkOrderActor Crew = new(IsAssignee: true, CanSupervise: false, CanManage: false);
    private static readonly WorkOrderActor Bystander = new(IsAssignee: false, CanSupervise: false, CanManage: false);
    private static readonly WorkOrderActor Supervisor = new(IsAssignee: false, CanSupervise: true, CanManage: false);
    private static readonly WorkOrderActor Manager = new(IsAssignee: false, CanSupervise: true, CanManage: true);

    [Theory]
    [InlineData(WorkOrderStatus.Open, WorkOrderTransition.Start, WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.InProgress, WorkOrderTransition.Block, WorkOrderStatus.Blocked)]
    [InlineData(WorkOrderStatus.Blocked, WorkOrderTransition.Unblock, WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.InProgress, WorkOrderTransition.Complete, WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Completed, WorkOrderTransition.Close, WorkOrderStatus.Closed)]
    [InlineData(WorkOrderStatus.Completed, WorkOrderTransition.Reopen, WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Closed, WorkOrderTransition.Reopen, WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Blocked, WorkOrderTransition.Cancel, WorkOrderStatus.Cancelled)]
    public void Valid_transitions_have_targets(WorkOrderStatus from, WorkOrderTransition t, WorkOrderStatus to) =>
        Assert.Equal(to, WorkOrderStateMachine.Target(from, t));

    [Theory]
    [InlineData(WorkOrderStatus.Open, WorkOrderTransition.Complete)]
    [InlineData(WorkOrderStatus.Closed, WorkOrderTransition.Close)]
    [InlineData(WorkOrderStatus.Closed, WorkOrderTransition.Cancel)]
    [InlineData(WorkOrderStatus.Cancelled, WorkOrderTransition.Reopen)]
    [InlineData(WorkOrderStatus.Cancelled, WorkOrderTransition.Start)]
    [InlineData(WorkOrderStatus.Blocked, WorkOrderTransition.Complete)]
    [InlineData(WorkOrderStatus.Completed, WorkOrderTransition.Cancel)]
    public void Invalid_transitions_have_no_target(WorkOrderStatus from, WorkOrderTransition t) =>
        Assert.Null(WorkOrderStateMachine.Target(from, t));

    [Fact]
    public void Assignee_can_progress_but_not_close_or_cancel()
    {
        Assert.Equal(new[] { WorkOrderTransition.Block, WorkOrderTransition.Complete }, WorkOrderStateMachine.Available(WorkOrderStatus.InProgress, Crew));
        Assert.Empty(WorkOrderStateMachine.Available(WorkOrderStatus.Completed, Crew));
        Assert.Empty(WorkOrderStateMachine.Available(WorkOrderStatus.InProgress, Bystander));
    }

    [Fact]
    public void Only_managers_reopen_closed_work()
    {
        Assert.False(WorkOrderStateMachine.IsPermitted(WorkOrderStatus.Closed, WorkOrderTransition.Reopen, Supervisor));
        Assert.True(WorkOrderStateMachine.IsPermitted(WorkOrderStatus.Closed, WorkOrderTransition.Reopen, Manager));
        Assert.True(WorkOrderStateMachine.IsPermitted(WorkOrderStatus.Completed, WorkOrderTransition.Reopen, Supervisor));
        Assert.Empty(WorkOrderStateMachine.Available(WorkOrderStatus.Cancelled, Manager));
    }

    [Theory]
    [InlineData(0, 100, WorkOrderStatus.InProgress, 0)]
    [InlineData(50, 200, WorkOrderStatus.InProgress, 25)]
    [InlineData(500, 200, WorkOrderStatus.InProgress, 100)]
    [InlineData(10, 200, WorkOrderStatus.Completed, 100)]
    [InlineData(10, 0, WorkOrderStatus.Open, 0)]
    public void Percent_complete_is_capped_and_done_is_100(decimal reported, decimal planned, WorkOrderStatus status, int expected) =>
        Assert.Equal(expected, WorkOrderService.PercentOf(reported, planned, status));
}

public sealed class OutboxBackoffTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    [InlineData(10, 300)]
    public void Backoff_is_exponential_and_capped(int attempt, double expectedSeconds)
    {
        var options = new OutboxOptions { BaseDelaySeconds = 5, MaxDelaySeconds = 300 };
        Assert.Equal(expectedSeconds, options.BackoffFor(attempt, 0.5).TotalSeconds, 3);
        Assert.InRange(options.BackoffFor(attempt, 0).TotalSeconds, expectedSeconds * 0.9 - 0.001, expectedSeconds);
        Assert.InRange(options.BackoffFor(attempt, 1).TotalSeconds, expectedSeconds, expectedSeconds * 1.1 + 0.001);
    }
}

public sealed class AuditDiffTests
{
    [Fact]
    public void Diff_keeps_only_changed_fields()
    {
        var (before, after) = AuditWriter.Diff(
            new Dictionary<string, object?> { ["title"] = "A", ["priority"] = WorkOrderPriority.High, ["qty"] = 10m },
            new Dictionary<string, object?> { ["title"] = "A", ["priority"] = WorkOrderPriority.Low, ["qty"] = 10.00m });
        Assert.Equal(new[] { "priority" }, before!.Keys.ToArray());
        Assert.Equal(WorkOrderPriority.Low, after!["priority"]);
    }

    [Fact]
    public void Create_keeps_the_full_after_snapshot()
    {
        var (before, after) = AuditWriter.Diff(null, new Dictionary<string, object?> { ["title"] = "New" });
        Assert.Null(before);
        Assert.Equal("New", after!["title"]);
    }
}

public sealed class BlockerSummaryRuleTests
{
    [Theory]
    [InlineData("Short 40 sheets of board; delivery slipped", "materials")]
    [InlineData("Waiting on city inspection booking", "inspection")]
    [InlineData("RFI 14 open with the structural engineer", "design")]
    [InlineData("Scaffold blocking ceiling access", "access")]
    [InlineData("Snow and wind closed the roof", "weather")]
    [InlineData("Something unusual", "other")]
    public void Classifies_blockers_by_keyword(string text, string expected) =>
        Assert.Equal(expected, RulesBasedBlockerSummaryProvider.Classify(text));

    [Fact]
    public async Task Summary_cites_only_context_evidence_and_flags_late_blocked_work()
    {
        var wo = Guid.NewGuid();
        var log = Guid.NewGuid();
        var mr = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 15);
        var context = new BlockerContext("RBL", today,
            [new BlockerDto(log, wo, "RBL-007", "Drywall L2", WorkOrderStatus.Blocked, "Short 40 sheets of board", "Rosa", DateTimeOffset.UtcNow.AddDays(-6), 6)],
            [new BlockedWork(wo, "RBL-007", "Drywall L2", WorkOrderStatus.Blocked, today.AddDays(-4), "Drywall complete", today.AddDays(-6), ["RBL-009"])],
            [new PendingMaterial(mr, wo, "RBL-007", "40 sheets board", 4)]);

        var summary = await new RulesBasedBlockerSummaryProvider(TimeProvider.System).SummarizeAsync(context, CancellationToken.None);
        Assert.Equal("Rules-based summary", summary.Label);
        Assert.Contains(summary.Points, p => p.Severity == "critical" && p.Text.Contains("past due"));
        Assert.Contains(summary.Points, p => p.Text.Contains("RBL-009"));
        Assert.Contains(summary.SuggestedActions, a => a.Contains("pending material request"));
        Assert.All(summary.Points, p => Assert.NotEmpty(p.Evidence));

        // Evidence outside the authorized context (e.g. invented by an optional AI provider) is dropped.
        var tampered = summary with
        {
            Points = [new SummaryPointDto("info", "Invented", [new EvidenceDto("workOrder", Guid.NewGuid(), "X")]), .. summary.Points],
        };
        var filtered = BlockerSummaryService.FilterEvidence(tampered, context);
        Assert.DoesNotContain(filtered.Points, p => p.Text == "Invented");
        Assert.Equal(summary.Points.Count, filtered.Points.Count);
    }
}
