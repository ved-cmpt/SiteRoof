-- Completion by work package (e.g. Drywall, Electrical) for the project dashboard.
-- Reported quantity is aggregated once per work order with OUTER APPLY, which uses
-- IX_ProgressLogs_WorkOrderId_CreatedAt, then rolled up per package.
-- PercentComplete averages per-work-order completion: done = 100, otherwise reported/planned capped at 100.
CREATE OR ALTER VIEW dbo.vw_WorkPackageCompletion
AS
SELECT
    w.ProjectId,
    w.WorkPackage,
    COUNT(*)                                                                              AS TotalWorkOrders,
    SUM(CASE WHEN w.Status IN ('Completed', 'Closed') THEN 1 ELSE 0 END)                  AS DoneWorkOrders,
    SUM(CASE WHEN w.Status IN ('Open', 'InProgress', 'Blocked')
              AND w.DueDate < CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Mountain Standard Time' AS date)
             THEN 1 ELSE 0 END)                                                           AS OverdueWorkOrders,
    CAST(SUM(w.PlannedQuantity) AS decimal(12, 2))                                        AS PlannedQuantity,
    CAST(ISNULL(SUM(r.Reported), 0) AS decimal(12, 2))                                    AS ReportedQuantity,
    CAST(AVG(CASE
                 WHEN w.Status IN ('Completed', 'Closed') THEN 100.0
                 WHEN w.PlannedQuantity > 0 AND ISNULL(r.Reported, 0) >= w.PlannedQuantity THEN 100.0
                 WHEN w.PlannedQuantity > 0 THEN ISNULL(r.Reported, 0) * 100.0 / w.PlannedQuantity
                 ELSE 0.0
             END) AS decimal(12, 2))                                                      AS PercentComplete
FROM dbo.WorkOrders AS w
OUTER APPLY (
    SELECT SUM(l.Quantity) AS Reported
    FROM dbo.ProgressLogs AS l
    WHERE l.WorkOrderId = w.Id
) AS r
WHERE w.Status <> 'Cancelled'
GROUP BY w.ProjectId, w.WorkPackage;
