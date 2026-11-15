using SiteRoof.Api.Domain;

namespace SiteRoof.Api.Features.WorkOrders;

/// <summary>Who is asking, relative to one work order.</summary>
public sealed record WorkOrderActor(bool IsAssignee, bool CanSupervise, bool CanManage);

/// <summary>
/// Pure work-order lifecycle rules (no I/O) so they can be unit-tested exhaustively.
/// Preconditions that need data (dependencies, unverified logs) are checked by the service.
/// </summary>
public static class WorkOrderStateMachine
{
    public static WorkOrderStatus? Target(WorkOrderStatus from, WorkOrderTransition transition) => (from, transition) switch
    {
        (WorkOrderStatus.Open, WorkOrderTransition.Start) => WorkOrderStatus.InProgress,
        (WorkOrderStatus.InProgress, WorkOrderTransition.Block) => WorkOrderStatus.Blocked,
        (WorkOrderStatus.Blocked, WorkOrderTransition.Unblock) => WorkOrderStatus.InProgress,
        (WorkOrderStatus.InProgress, WorkOrderTransition.Complete) => WorkOrderStatus.Completed,
        (WorkOrderStatus.Completed, WorkOrderTransition.Reopen) => WorkOrderStatus.InProgress,
        (WorkOrderStatus.Closed, WorkOrderTransition.Reopen) => WorkOrderStatus.InProgress,
        (WorkOrderStatus.Completed, WorkOrderTransition.Close) => WorkOrderStatus.Closed,
        (WorkOrderStatus.Open or WorkOrderStatus.InProgress or WorkOrderStatus.Blocked, WorkOrderTransition.Cancel) => WorkOrderStatus.Cancelled,
        _ => null,
    };

    public static bool IsPermitted(WorkOrderStatus from, WorkOrderTransition transition, WorkOrderActor actor) => transition switch
    {
        WorkOrderTransition.Start or WorkOrderTransition.Block or WorkOrderTransition.Unblock or WorkOrderTransition.Complete
            => actor.IsAssignee || actor.CanSupervise,
        WorkOrderTransition.Reopen when from == WorkOrderStatus.Closed => actor.CanManage,
        _ => actor.CanSupervise,
    };

    public static IReadOnlyList<WorkOrderTransition> Available(WorkOrderStatus from, WorkOrderActor actor) =>
        Enum.GetValues<WorkOrderTransition>()
            .Where(t => Target(from, t) is not null && IsPermitted(from, t, actor))
            .ToList();

    public static bool IsDone(WorkOrderStatus s) => s is WorkOrderStatus.Completed or WorkOrderStatus.Closed;

    public static bool IsActive(WorkOrderStatus s) => s is WorkOrderStatus.Open or WorkOrderStatus.InProgress or WorkOrderStatus.Blocked;
}
