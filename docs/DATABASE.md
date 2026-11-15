# Database

SQL Server 2022 locally (Docker, host port 1434) and Azure SQL Database when hosted. Schema is owned by
EF Core migrations in `api/SiteRoof.Api/Data/Migrations`; hand-written T-SQL lives in
`api/SiteRoof.Api/Data/Sql` and is applied by the `DashboardViews` migration.

```powershell
docker compose up -d db
./scripts/migrate.ps1          # applies all migrations and exits
```

## Tables

| Table | Key constraints |
| --- | --- |
| `Tenants` | `IX (IsDemo, ExpiresAt)` for cleanup |
| `AspNetUsers` (+ Identity tables) | `TenantId` FK (restrict); `IX (TenantId, PersonaKey)` |
| `Projects` | `UQ (TenantId, Code)`; `CHECK TargetDate >= StartDate`; status CHECK |
| `ProjectMemberships` | `PK (ProjectId, UserId)`; role CHECK; cascade from project |
| `Milestones` | `IX (ProjectId, DueDate)`; status CHECK |
| `WorkOrders` | `UQ (ProjectId, Number)`; `CHECK PlannedQuantity >= 0`, `Number > 0`; status/priority CHECKs; `RowVersion rowversion`; `IX (ProjectId, Status, DueDate) INCLUDE (WorkPackage, Priority)`; `IX (AssigneeId, Status)` |
| `WorkOrderComments` | `IX (WorkOrderId, CreatedAt)` |
| `WorkOrderDependencies` | `PK (WorkOrderId, DependsOnWorkOrderId)`; `CHECK WorkOrderId <> DependsOnWorkOrderId` |
| `ProgressLogs` | `CHECK Quantity >= 0`; `IX (WorkOrderId, CreatedAt)` |
| `MaterialRequests` | `UQ (WorkOrderId, ClientRequestId)` (submit once); `CHECK Quantity > 0`; decision CHECK (decided rows must have `DecidedById` and `DecidedAt`); `RowVersion`; `IX (TenantId, Status, RequestedAt)` |
| `IntegrationOutbox` | `UQ IdempotencyKey`; `CHECK Attempts >= 0`; status CHECK; `IX (Status, NextRetryAt)` for the dispatcher |
| `AuditEvents` | `IX (TenantId, CreatedAt)`, `IX (ProjectId, CreatedAt)`, `IX (EntityType, EntityId)` |
| `mockerp.MockErpRequisitions` | `UQ IdempotencyKey` — one requisition per key |
| `mockerp.MockErpCalls` | Every call received, with the status returned |

Status columns are stored as strings (readable in ad-hoc queries) and guarded by CHECK constraints so
the database rejects values the application would never write.

User foreign keys are `RESTRICT`/`NO ACTION`; project-owned rows cascade from `Projects`. Deleting a
whole tenant is done explicitly, children first, inside one transaction (`TenantDeleter`).

## Views used by the dashboards

### `dbo.vw_ProjectWorkOrderRollup`

One row per project with counts by status, overdue count and open critical count, computed in a single
pass with conditional aggregation:

```sql
SELECT p.Id AS ProjectId, p.TenantId,
       COUNT(w.Id) AS TotalWorkOrders,
       ISNULL(SUM(CASE WHEN w.Status = 'Open' THEN 1 ELSE 0 END), 0) AS OpenWorkOrders,
       ...
       ISNULL(SUM(CASE WHEN w.Status IN ('Open','InProgress','Blocked') AND w.DueDate < t.d THEN 1 ELSE 0 END), 0) AS OverdueWorkOrders
FROM dbo.Projects p
CROSS JOIN (SELECT CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Mountain Standard Time' AS date) AS d) t
LEFT JOIN dbo.WorkOrders w ON w.ProjectId = p.Id
GROUP BY p.Id, p.TenantId;
```

- "Today" is evaluated in Mountain time (the projects are in the Edmonton area), matching `SiteClock` in
  the API, so the API and the view agree on what is overdue.
- Callers always filter by `ProjectId` (after the authorization check), so SQL Server seeks
  `IX_WorkOrders_ProjectId_Status_DueDate`, which also covers `Priority`, instead of scanning.
- Used by `GET /api/projects`, `GET /api/overview` and `GET /api/projects/{id}/dashboard`.

### `dbo.vw_WorkPackageCompletion`

Completion by work package (Drywall, Electrical, …). Reported quantities are summed once per work order
with `OUTER APPLY` over `ProgressLogs` (seek on `IX_ProgressLogs_WorkOrderId_CreatedAt`), then rolled up
per package. `PercentComplete` averages per-work-order completion (done = 100, otherwise
reported/planned capped at 100), so a package with mixed units is still meaningful.

Full source: [`vw_ProjectWorkOrderRollup.sql`](../api/SiteRoof.Api/Data/Sql/vw_ProjectWorkOrderRollup.sql),
[`vw_WorkPackageCompletion.sql`](../api/SiteRoof.Api/Data/Sql/vw_WorkPackageCompletion.sql).

## Transactions and concurrency

- **Business change + audit + outbox** are added to one `DbContext` and saved with a single
  `SaveChanges`, which EF Core wraps in a transaction. An approval therefore commits the status change,
  the audit row and the outbox message together or not at all.
- **Optimistic concurrency**: `WorkOrders.RowVersion` and `MaterialRequests.RowVersion`. The service
  compares the client's version and also sets it as the original value, so the `UPDATE … WHERE
  RowVersion = @expected` fails if another writer slipped in between (→ `409 concurrency-conflict`).
- **Outbox claim** (`OutboxProcessor`):

  ```sql
  WITH due AS (
      SELECT TOP (@batch) Id, Status, LockedUntil
      FROM dbo.IntegrationOutbox WITH (ROWLOCK, READPAST, UPDLOCK)
      WHERE (Status = 'Pending' AND NextRetryAt <= @now)
         OR (Status = 'Processing' AND LockedUntil < @now)
      ORDER BY NextRetryAt)
  UPDATE due SET Status = 'Processing', LockedUntil = @lockUntil
  OUTPUT inserted.Id AS [Value];
  ```

  `READPAST` lets concurrent dispatchers skip rows another one has locked; the lease (`LockedUntil`)
  recovers rows if a dispatcher dies mid-delivery. All values are parameters.

## Seed data

`DemoSeeder` seeds one tenant at a time and is idempotent: projects are keyed by code (`RBL`, `NTS`,
`MCC`) within the tenant and skipped if present, so re-running never duplicates or resets a workspace.
Dates are relative to the seed date. Contents per tenant: 3 projects (one completed), 30 work orders,
11 milestones, 9 people, 5 pending material requests, 1 rejected, 3 approved (one with an ERP retry in
progress), comments, progress logs and history.
