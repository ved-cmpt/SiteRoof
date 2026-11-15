namespace SiteRoof.Api.Domain;

public enum ProjectStatus { Planning, Active, OnHold, Completed }

public enum ProjectRole { Manager, Supervisor, Crew }

public enum MilestoneStatus { Planned, InProgress, Completed }

public enum WorkOrderPriority { Low, Normal, High, Critical }

public enum WorkOrderStatus { Open, InProgress, Blocked, Completed, Closed, Cancelled }

public enum WorkOrderTransition { Start, Block, Unblock, Complete, Reopen, Close, Cancel }

public enum MaterialRequestStatus { Submitted, Approved, Rejected }

public enum OutboxStatus { Pending, Processing, Succeeded, Failed }

public enum ErpSimulatorMode
{
    /// <summary>Every call succeeds.</summary>
    Normal,

    /// <summary>The first call for each idempotency key returns 503; later calls succeed.</summary>
    FailFirstAttempt,

    /// <summary>Every call returns 503 (exercises bounded retries and dead-lettering).</summary>
    Unavailable,
}

public static class TenantRoles
{
    public const string Admin = "Admin";
    public const string ProjectManager = "ProjectManager";
    public const string SiteSupervisor = "SiteSupervisor";
    public const string CrewMember = "CrewMember";

    public static readonly string[] All = [Admin, ProjectManager, SiteSupervisor, CrewMember];
}

public static class AuditActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string StatusChanged = "status_changed";
    public const string Assigned = "assigned";
    public const string Commented = "commented";
    public const string ProgressReported = "progress_reported";
    public const string ProgressVerified = "progress_verified";
    public const string BlockerResolved = "blocker_resolved";
    public const string DependencyAdded = "dependency_added";
    public const string DependencyRemoved = "dependency_removed";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string ErpAcknowledged = "erp_acknowledged";
    public const string ErpRetryScheduled = "erp_retry_scheduled";
    public const string ErpFailed = "erp_failed";
    public const string ErpManualRetry = "erp_manual_retry";
    public const string SettingsChanged = "settings_changed";
}
