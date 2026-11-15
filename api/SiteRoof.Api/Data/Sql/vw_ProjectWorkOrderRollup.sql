-- Per-project work-order rollup used by GET /api/projects, /api/overview and /api/projects/{id}/dashboard.
-- One pass over WorkOrders per project with conditional aggregation (no per-status subqueries).
-- "Today" is evaluated in Mountain time (Edmonton) to match SiteClock in the API.
-- Supported by IX_WorkOrders_ProjectId_Status_DueDate (INCLUDE WorkPackage, Priority).
CREATE OR ALTER VIEW dbo.vw_ProjectWorkOrderRollup
AS
WITH today AS (
    SELECT CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Mountain Standard Time' AS date) AS d
)
SELECT
    p.Id       AS ProjectId,
    p.TenantId AS TenantId,
    COUNT(w.Id)                                                                     AS TotalWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status = 'Open'       THEN 1 ELSE 0 END), 0)             AS OpenWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status = 'InProgress' THEN 1 ELSE 0 END), 0)             AS InProgressWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status = 'Blocked'    THEN 1 ELSE 0 END), 0)             AS BlockedWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status = 'Completed'  THEN 1 ELSE 0 END), 0)             AS CompletedWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status = 'Closed'     THEN 1 ELSE 0 END), 0)             AS ClosedWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status = 'Cancelled'  THEN 1 ELSE 0 END), 0)             AS CancelledWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status IN ('Open', 'InProgress', 'Blocked')
                     AND w.DueDate < t.d THEN 1 ELSE 0 END), 0)                     AS OverdueWorkOrders,
    ISNULL(SUM(CASE WHEN w.Status IN ('Open', 'InProgress', 'Blocked')
                     AND w.Priority = 'Critical' THEN 1 ELSE 0 END), 0)             AS CriticalOpenWorkOrders
FROM dbo.Projects AS p
CROSS JOIN today AS t
LEFT JOIN dbo.WorkOrders AS w ON w.ProjectId = p.Id
GROUP BY p.Id, p.TenantId;
