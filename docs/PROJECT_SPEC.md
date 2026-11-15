# SiteRoof — Project Specification

> Source of truth for scope, rules and acceptance. Status of each item lives in
> [`docs/STATUS.md`](STATUS.md). This file describes targets; it is not a claim
> that anything is finished.

## 1. Purpose

SiteRoof is an independent, public software-engineering portfolio project: a
**construction operations platform for coordinating projects, milestones,
assigned work orders, site progress and limited material approvals across office
and field teams.**

Tagline: **"Keep site work, decisions, and progress in one place."**

It was built to demonstrate the kind of internal business software a general
contractor relies on — well-designed frontends, REST APIs, relational data,
reliable enterprise workflows, security, automated tests, troubleshooting and
deployment — for a Software Developer Student application (Edmonton, AB,
January 2027 work term).

### Independence and honesty rules

- SiteRoof is **not** affiliated with, endorsed by, or built for any construction
  company. It does not recreate any proprietary system.
- No company branding, logos, project data, contracts, documents or employee
  identities. All projects, people, addresses and records are **fictional**.
- The project runs and deploys alone, with its own repository and database.
- Never invent metrics (performance, uptime, users, accuracy). Only measured
  numbers may be published, with the command that produced them.
- Deployment is not claimed until it has been verified with a smoke test.
- Rules-based features are labelled "rules-based", never "AI".

### Product boundary

SiteRoof owns day-to-day **project execution and cross-team coordination**. It is
not a purchasing ERP, document control system, data-science tool or offline crew
reporting app. Material requests are deliberately limited: submit → approve or
reject → acknowledgement from a mock ERP. No purchase-order accounting.

## 2. Users and roles

| Role | Capabilities |
| --- | --- |
| **Admin** | Manages demo users and tenant (workspace) settings; can read every project in the tenant. |
| **Project Manager** | Creates projects, plans milestones, assigns activities, approves or rejects material requests, reviews progress. |
| **Site Supervisor** | Creates and assigns work orders, verifies progress, resolves blockers, closes work. |
| **Crew Member** | Views assignments, reports progress and blockers, comments, requests materials. |
| **Guest Demo** | A visitor who clicked "Try Demo": gets an isolated, freshly seeded tenant and can switch between that tenant's Manager, Supervisor and Crew personas. |

Two levels of role exist:

1. **Tenant role** (ASP.NET Core Identity role): Admin, ProjectManager,
   SiteSupervisor, CrewMember. Governs tenant-wide actions (create project,
   admin settings).
2. **Project membership role** (`ProjectMembership.Role`): Manager, Supervisor,
   Crew. Governs everything inside a project. A user without a membership cannot
   see the project at all (except tenant Admins, read-only oversight).

### Capability matrix (inside a project)

| Action | Manager | Supervisor | Crew |
| --- | --- | --- | --- |
| View project, milestones, roster, board, activity | ✔ | ✔ | ✔ |
| Edit project details / create milestones | ✔ | — | — |
| Create work order, assign, prioritize, set due date | ✔ | ✔ | — |
| Start / block / complete a work order | ✔ | ✔ | assignee only |
| Close or cancel a work order | ✔ | ✔ | — |
| Reopen a closed work order (reason required, audited) | ✔ | — | — |
| Add progress log | ✔ | ✔ | assignee only |
| Verify progress / resolve blocker | ✔ | ✔ | — |
| Comment | ✔ | ✔ | ✔ |
| Add / remove dependencies | ✔ | ✔ | — |
| Submit material request | ✔ | ✔ | assignee only |
| Approve / reject material request | ✔ | — | — |
| Retry a failed ERP transmission | ✔ | — | — |

All checks are enforced **server-side** on every read and write (object-level
authorization). Hiding buttons in the UI is a convenience only.

## 3. Modules

- **Projects**: code, title, description, client (fictional), site address
  (fictional), dates, status, team roster, milestones.
- **Work orders**: create, prioritize, assign (project members only), comment,
  due date, work package, planned quantity + unit, dependencies, status
  transitions and closure.
- **Progress logs**: quantity completed, blocker text, note, timestamp, reporter;
  supervisor verification; blocker resolution.
- **Material requests**: linked to a work order; submitted once; approved or
  rejected by a project manager; approval enqueues a transactional outbox event
  that a background worker transmits to the mock ERP.
- **Integrations**: transactional outbox, background dispatcher with bounded
  exponential-backoff retries, idempotency keys, self-hosted mock ERP endpoint
  with fault injection, integration health endpoint.
- **Dashboards**: overdue work, completion by work package, open blockers,
  approval backlog, recent activity — all computed from persisted rows.
- **Activity feed and audit trail**: actor, timestamp, entity, action, changed
  fields with before/after JSON.
- **Open-blocker summary**: deterministic, evidence-linked, labelled
  "Rules-based summary". An optional AI provider interface exists and is
  disabled by default; core functionality never depends on it. AI may summarize;
  it never approves anything.
- **Demo lifecycle**: one-click isolated guest tenant, persona switcher, Reset My
  Demo, expiry and background cleanup, provisioning rate limits and quotas.

## 4. Entities and invariants

| Entity | Key fields |
| --- | --- |
| Tenant | Id, Name, IsDemo, ErpSimulatorMode, CreatedAt, ExpiresAt |
| User (Identity) | Id, TenantId, DisplayName, JobTitle, Email (synthetic for demo), PersonaKey |
| Project | Id, TenantId, Code (unique per tenant), Title, Description, ClientName, SiteAddress, City, Status, StartDate, TargetDate, CreatedAt |
| ProjectMembership | ProjectId, UserId (composite PK), Role, Trade |
| Milestone | Id, ProjectId, Name, DueDate, Status, CompletedAt |
| WorkOrder | Id, TenantId, ProjectId, Number (unique per project), Title, Description, WorkPackage, AssigneeId, MilestoneId, DueDate, Priority, Status, PlannedQuantity, Unit, RowVersion, CreatedById, CreatedAt, CompletedAt, ClosedAt |
| WorkOrderComment | Id, WorkOrderId, AuthorId, Body, CreatedAt |
| WorkOrderDependency | WorkOrderId, DependsOnWorkOrderId (composite PK, no self-reference) |
| ProgressLog | Id, WorkOrderId, ReporterId, Quantity, Blocker, Note, CreatedAt, VerifiedById, VerifiedAt, BlockerResolvedById, BlockerResolvedAt |
| MaterialRequest | Id, TenantId, WorkOrderId, ItemName, Quantity, Unit, Justification, Status, RequestedById, RequestedAt, DecidedById, DecidedAt, DecisionNote, ClientRequestId, ExternalReference, RowVersion |
| IntegrationOutbox | Id, TenantId, EventType, AggregateId, Payload, IdempotencyKey (unique), Status, Attempts, NextRetryAt, LastError, ExternalReference, CreatedAt, ProcessedAt |
| AuditEvent | Id, TenantId, ProjectId, ActorId, EntityType, EntityId, Action, Summary, BeforeJson, AfterJson, CreatedAt |
| MockErpRequisition / MockErpCall | Self-hosted mock ERP storage: one requisition per idempotency key; one row per received call |

Invariants (enforced in the service layer **and** where possible in the
database):

- Every work order belongs to a project (FK, NOT NULL); `TenantId` of a work
  order equals its project's tenant.
- Assignment must be to a member of that project (service check).
- Quantities cannot be negative (CHECK constraints: progress `Quantity >= 0`,
  planned quantity `>= 0`, material request `Quantity > 0`).
- A closed or cancelled work order cannot be silently edited (HTTP 409
  `work-order-locked`); only an explicit, audited manager reopen is allowed for
  closed work orders.
- Approval must be authorized (project Manager membership only; crew → 403).
- A material request is submitted once (unique `ClientRequestId` per work
  order) and decided once (repeat of the same decision is an idempotent no-op;
  a contradictory decision is 409).
- ERP events are idempotent (unique outbox `IdempotencyKey`; the mock ERP stores
  one requisition per key and returns the same reference on repeats).
- Optimistic concurrency on work orders and material requests (`rowversion`);
  a stale version returns 409 with a readable conflict message.
- Dependencies stay within one project and cannot form cycles.
- Project `TargetDate >= StartDate`.

## 5. State machines

### Work order

```
Open ──start──▶ InProgress ──complete──▶ Completed ──close──▶ Closed
  │               ▲   │                     │                  │
  │          unblock  block                reopen            reopen (manager,
  │               │   ▼                     ▼                  reason required)
  │             Blocked                 InProgress            ▼
  └──cancel──▶ Cancelled ◀──cancel── (InProgress | Blocked)  InProgress
```

- `start`: requires an assignee.
- `complete`: requires every dependency to be Completed or Closed.
- `close`: requires status Completed, no unverified progress logs, no open
  blockers and no pending material requests.
- Closed and Cancelled are locked: field edits return 409.

### Material request

```
Submitted ──approve (Manager)──▶ Approved ──outbox──▶ ERP: Pending → Retrying → Sent
    └──────reject (Manager)───▶ Rejected                         └─▶ Failed (after max attempts; manual retry)
```

### Integration outbox

`Pending → Processing → Succeeded`, or `Processing → Pending (attempt+1,
next_retry_at = now + backoff)`, or `→ Failed` after the maximum attempts.
Backoff: `base * 2^(attempt-1)` capped, with small jitter.

### Milestone

`Planned → InProgress → Completed` (overdue is computed: due date passed and not
completed).

## 6. API surface

All routes are under `/api`, JSON, cookie-authenticated, CSRF-protected for
unsafe methods (`X-XSRF-TOKEN` header). Errors are RFC 9457 ProblemDetails.
Lists support `page`, `pageSize` and filters.

| Method | Route | Purpose |
| --- | --- | --- |
| POST | /api/demo/start | Provision an isolated guest tenant and sign in (rate-limited) |
| GET | /api/demo/scenario | Guided scenario steps + personas for the current demo tenant |
| POST | /api/demo/persona | Switch to another persona of the same tenant |
| POST | /api/demo/reset | Replace the current demo tenant with a fresh one |
| POST | /api/auth/login, /api/auth/logout; GET /api/auth/me | Session management |
| GET/POST | /api/projects | List accessible projects / create project |
| GET/PATCH | /api/projects/{id} | Project detail (roster, milestones) / edit |
| GET/POST | /api/projects/{id}/milestones | Milestones |
| PATCH | /api/milestones/{id} | Update milestone status/date |
| GET/POST | /api/projects/{id}/work-orders | Board (filter: status, assignee, package, overdue, search) / create |
| GET/PATCH | /api/work-orders/{id} | Detail / edit fields (version required) |
| POST | /api/work-orders/{id}/transitions | Status transition (version required) |
| GET/POST | /api/work-orders/{id}/comments | Comments |
| POST/DELETE | /api/work-orders/{id}/dependencies[/{dependsOnId}] | Dependencies |
| GET/POST | /api/work-orders/{id}/progress | Progress logs |
| POST | /api/progress/{id}/verify, /api/progress/{id}/resolve-blocker | Supervisor actions |
| GET/POST | /api/work-orders/{id}/material-requests | Material requests for a work order |
| GET | /api/material-requests | Approvals list (filter by status) |
| POST | /api/material-requests/{id}/decision | Approve / reject |
| GET | /api/overview | Tenant-wide KPIs over accessible projects |
| GET | /api/projects/{id}/dashboard | Project dashboard (from SQL view) |
| GET | /api/projects/{id}/activity, /api/activity | Activity feed |
| GET | /api/projects/{id}/blocker-summary | Rules-based open-blocker summary |
| GET | /api/integrations/health | Outbox counts, worker heartbeat, mock ERP reachability |
| POST | /api/integrations/outbox/{id}/retry | Manual retry of a failed transmission |
| GET/PATCH | /api/admin/tenant | Tenant settings (Admin) |
| POST | /mock-erp/requisitions | Self-hosted mock ERP (internal key required) |
| GET | /health, /health/ready | Liveness / readiness |

## 7. Screens

- `/` landing page: headline + value proposition, live interface preview,
  problem/solution, three role workflows, capability highlights, how the demo
  works, engineering proof, why it was built, accessible footer with GitHub
  link, stack, attribution and independent-project disclaimer.
- `/case-study`: problem, users, business rules, architecture / data model /
  deployment SVG diagrams, stack rationale, security, testing, limitations,
  walkthrough, source link, disclaimer.
- `/app/overview`: KPIs, project switcher, status chart, attention queue.
- `/app/projects`, `/app/projects/:id`: milestones, roster, progress,
  work-order board, dashboard, blocker summary.
- `/app/work-orders/:id`: assignment, status actions, progress history,
  blockers, comments, dependencies, material requests.
- `/app/approvals`: pending / approved / rejected requests with ERP status.
- `/app/activity`: audit feed with before/after diff.
- `/app/admin`: tenant settings, users, ERP simulator mode, demo controls.
- 403 and 404 pages; loading, empty and error states everywhere.

## 8. Seed data (per demo tenant, idempotent)

- **Riverbend Library Renovation** (RBL-2026) — fictional Edmonton public library
  renovation, active and **behind on drywall** (overdue drywall work orders,
  open material-shortage blocker).
- **Northgate Transit Shelter** (NTS-2026) — active, roughly on track.
- **Maplewood Community Centre** (MCC-2025) — completed project, all work closed.
- ≥ 25 work orders, ≥ 10 milestones, multiple roles, several overdue items,
  5 pending material requests, 1 rejected request, approved requests including
  one ERP event in retry state, one completed project.
- Dates are relative to the provisioning date so "overdue" stays meaningful.

## 9. Demo scenario

"Open Riverbend → inspect delayed work → submit progress → approve material
request → see dashboard and ERP status update."

Full journey:

1. A supervisor creates a work order and assigns an eligible crew member.
2. The crew member adds progress and a blocker; the supervisor verifies it.
3. A material request is sent to the manager and approved with an audit entry.
4. A transactional outbox event is processed by the resilient mock ERP; the
   integration log shows success or retry.
5. All dashboard figures are recalculated from persisted records.

## 10. Must-pass tests

1. A crew member cannot view another project by changing a URL ID.
2. Manager approvals are logged, idempotent and forbidden for crew.
3. The ERP simulator returns 503 once; the retry succeeds without a duplicate.
4. Two conflicting status edits resolve safely with a visible 409 conflict.
5. Creating and completing work orders updates the dashboard, database and
   activity feed.
6. Playwright: the full recruiter demo scenario above.

## 11. Non-functional requirements

- Angular (standalone, strict TypeScript, SCSS, Angular Material), ASP.NET Core
  on .NET 10, EF Core 10, SQL Server 2022. Modular monolith.
- Production: one container — the API serves the built Angular app from the
  same origin; cookie auth + antiforgery; no CORS dependency.
- Rate limits, input validation, security headers, structured logs, health
  checks, secrets from environment variables only.
- Responsive (mobile/tablet/desktop), keyboard usable, labelled forms, visible
  focus, adequate contrast, reduced-motion support.
- CI: backend build/test, frontend lint/test/build, Playwright, Docker build,
  final required `ci-ok` job; docs-only changes skip heavy jobs.

## 12. Phase backlog

- **Phase 1 — Foundation + vertical slice**: schema and migrations for all
  entities, SQL view for dashboard rollups, Identity cookies + RBAC + tenant and
  membership checks, guest demo isolation with persona switcher, idempotent
  seed, REST API (projects, milestones, work orders, progress, dashboard),
  Angular landing, case study, app shell, overview, projects, work-order detail.
- **Phase 2 — Domain workflows**: full supervisor/crew/manager journey, material
  requests + approvals, outbox + worker + mock ERP with retries and idempotency,
  approvals screen, integration health, comments, dependencies, closure rules,
  optimistic concurrency UI, activity + audit trail, complete dashboards,
  accessibility and visual polish.
- **Phase 3 — Hardening + presentation**: rules-based blocker summary with an
  optional disabled AI interface, demo reset/expiry/cleanup/quotas, security
  hardening and SECURITY.md, all must-pass tests, Dockerfile, docker-compose,
  GitHub Actions, Dependabot, deployment guide (not deployed), final case study,
  README with real screenshots, architecture and API docs, MIT license.

## 13. Owner-only steps

Create the GitHub repository and remote, choose and authorize hosting (Render
free web service + Azure SQL Database free offer are documented), set secrets in
the host's secret store, optionally provide an AI API key with a budget limit,
and approve any costs. Nothing is deployed or provisioned automatically.
