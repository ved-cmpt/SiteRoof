# Architecture

SiteRoof is a **modular monolith**: one ASP.NET Core process that serves the Angular build, the REST API,
two background workers and a self-hosted mock ERP endpoint, backed by one SQL Server database.

```
Browser (Angular SPA)
   │  HTTPS, same origin, cookie + X-XSRF-TOKEN
   ▼
ASP.NET Core (.NET 10) ─────────────────────────────────────────────┐
   static files (Angular dist)                                       │
   middleware: forwarded headers → exception handler → security      │
     headers → request log → rate limiter → authentication →         │
     CSRF validation → authorization                                 │
   Features/* controllers → services                                 │
     ProjectAccess (tenant + membership) · AuditWriter · SiteClock   │
   EF Core 10 (tenant query filters, rowversion)                     │
   BackgroundService: OutboxDispatcher ──HTTP──► /mock-erp ◄─────────┘
   BackgroundService: DemoCleanupService
   ▼
SQL Server 2022 / Azure SQL (tables, constraints, T-SQL views, mockerp schema)
```

## Backend layout (`api/SiteRoof.Api`)

| Folder | Responsibility |
| --- | --- |
| `Domain/` | Entities and enums (no framework logic) |
| `Data/` | `SiteRoofDbContext` (configuration, query filters, views), migrations, `Sql/*.sql` |
| `Auth/` | `RequestContext` (current user / tenant), claims factory, `ProjectAccess`, typed errors |
| `Common/` | `AuditWriter`, paging, ProblemDetails mapping, `SiteClock`, demo quotas |
| `Features/Projects` | Projects, milestones, roster |
| `Features/WorkOrders` | Work orders, pure `WorkOrderStateMachine`, comments, dependencies, progress |
| `Features/Materials` | Material requests and decisions (writes the outbox row) |
| `Features/Integrations` | `HttpErpClient`, `OutboxProcessor`, `OutboxDispatcher`, `MockErpController`, health |
| `Features/Dashboard` | Overview and project dashboards (from the SQL views) |
| `Features/Activity` | Audit feed |
| `Features/Insights` | `IBlockerSummaryProvider`, rules-based provider, evidence filter |
| `Features/Demo` | Provisioner, seeder, tenant deleter, cleanup worker, demo endpoints |
| `Features/Auth`, `Features/Admin` | Session / CSRF tokens, workspace settings |
| `Infrastructure/` | Rate limits, CSRF middleware, security headers, request logging |

Controllers are thin; services own authorization, validation, business rules and the unit of work.

## Authorization model

1. **Authentication**: Identity cookie. Claims include `siteroof:tenant`.
2. **Tenant isolation**: global query filters on every tenant-owned entity
   (`TenantId == CurrentTenantId`). With no signed-in user the filter matches nothing (fails closed).
   Background code opts out explicitly with `IgnoreQueryFilters()` and scopes by tenant itself.
3. **Object-level checks**: `ProjectAccess.ForProjectAsync(projectId)` runs before any project data is
   read or written. Unknown or other-tenant ids → 404; same tenant without membership → 403.
   Capabilities come from the project membership role (Manager / Supervisor / Crew); tenant roles gate
   workspace actions (create project, admin).
4. The Angular route guards and hidden buttons are conveniences only; the server re-checks everything.

## Work-order lifecycle

`WorkOrderStateMachine` is a pure function of (status, transition, actor) — exhaustively unit-tested.
The service adds data-dependent preconditions (dependencies complete, closure checks, reasons) and
concurrency (`version`). Progress reports drive status in the field-friendly direction: the first
progress starts open work; a reported blocker blocks in-progress work; resolving the last blocker
resumes it. All changes write audit events with before/after values.

## Material approval and ERP integration

```
Manager approves ──► one SaveChanges: request Approved + AuditEvent + IntegrationOutbox(Pending, key=mr-approved:{id})
OutboxDispatcher (every 2 s) ──► claim due rows (UPDATE … OUTPUT, READPAST)
   └─► HttpErpClient POST /mock-erp/requisitions (Idempotency-Key, X-Mock-Erp-Key, 10 s timeout)
        ├─ 2xx  → Succeeded, ExternalReference stored on request, audit "erp_acknowledged"
        ├─ 5xx/429/timeout → Pending, Attempts+1, NextRetryAt = now + 5 s·2^(n-1) ±10 % (cap 300 s), audit "erp_retry_scheduled"
        └─ attempts = 5 or 4xx → Failed (dead letter), manual retry by the project manager
Mock ERP: per-tenant mode (Normal / FailFirstAttempt / Unavailable); one requisition per key; repeats return the same reference.
```

Delivery is at-least-once; the idempotency key makes the effect exactly-once at the ERP. The worker uses
`TimeProvider`, so tests drive it deterministically with a fake clock.

## Guest demo lifecycle

- `POST /api/demo/start` (rate-limited per client, capacity-capped) creates a tenant, nine password-less
  users with roles, and the seeded scenario, then signs the visitor in as the chosen persona.
- The persona switcher signs in as another persona **of the same tenant**; nothing crosses tenants.
- `POST /api/demo/reset` provisions a fresh tenant, signs in, then deletes the old one.
- Tenants expire after 8 hours (configurable); `DemoCleanupService` deletes expired tenants every 10
  minutes. Per-tenant quotas cap projects, work orders, comments, progress logs and material requests.

## Rules-based blocker summary and the optional AI provider

`GET /api/projects/{id}/blocker-summary` builds a `BlockerContext` (open blockers, the blocked work,
dependents, milestone dates, pending material requests) after the normal authorization check, then asks an
`IBlockerSummaryProvider`. The registered provider is `RulesBasedBlockerSummaryProvider`: keyword
categories, age, overdue blocked work, downstream dependents and milestone exposure, each statement with
evidence links. The response is labelled `"Rules-based summary"`.

To add an AI-backed provider later (disabled by default, never required):

1. Implement `IBlockerSummaryProvider` with `Name = "ai"` and `IsEnabled` true only when an API key is set
   through configuration/secret store.
2. Register it in `Program.cs` and set `Insights__AiProvider=ai`.
3. Send only the `BlockerContext` (already authorized, minimal), request structured JSON output, validate it
   against `BlockerSummaryDto`, label it as AI-generated, and keep rate limits per tenant.
4. `BlockerSummaryService.FilterEvidence` already drops any evidence id that is not in the context, and any
   exception falls back to the rules-based summary. The provider may summarize; it never approves or changes data.

## Frontend layout (`web/src/app`)

| Folder | Contents |
| --- | --- |
| `core/` | `ApiService` (typed calls, `ApiError`), `SessionService` (signals), guards, interceptor, `Loader` |
| `shared/` | Icons (inline SVG generated from Material Symbols), status chips, KPI tiles, states, charts |
| `public/` | Landing, case study, demo start, sign-in, 403/404 |
| `shell/` | Authenticated layout: navigation, project switcher, persona switcher, demo strip, guided-demo panel |
| `features/` | Overview, projects, project detail (board / dashboard / milestones & team / activity), work order, approvals, activity, admin, dialogs |

Standalone components, OnPush change detection, signals, zoneless change detection, lazy-loaded routes,
strict TypeScript and strict templates. Fonts and icons are bundled (no third-party requests), which keeps
the Content-Security-Policy strict.

### Design system

The visual language is "a calm product UI printed on a construction drawing". Tokens live in
`web/src/styles/_tokens.scss`; `_material.scss` re-skins Angular Material to them; `_base.scss` holds element
styles and shared primitives.

| Token group | Values |
| --- | --- |
| Surfaces | Drawing-paper background `#F6F4EF`, white surfaces, subtle wells `#F0EDE6`, 1px hairlines `#E2DED6` / `#CFC9BD` |
| Ink | Text `#16181D`, secondary `#3D4048`, warm-gray tertiary `#67625A` (5.5:1 on the background) |
| Accent | Construction orange `#D9480F` for non-text marks (focus ring, active nav marker, overdue rules, callouts); `#B93D0B` for filled buttons and accent text (5.6:1 on white, WCAG AA) |
| Type | IBM Plex Sans (UI and headings) and IBM Plex Mono (codes, ids, quantities, dates, numbers), both self-hosted. In the app: three sizes per screen (20px title, 14px body, 12px small), weights 400/500 with 600 only for page titles |
| Motifs | Faint 24px drawing grid behind heroes, mono "sheet" labels, title-block footer, dimension-line divider; no gradients or heavy shadows |
| Spacing and shape | Spacing scale 4/8/12/16/24/32 (`--sr-s1`…`--sr-s6`), one 6px radius, 1px hairlines |
| Motion | 150 ms fades and colour transitions only, disabled under `prefers-reduced-motion` |

Status pills use muted tinted backgrounds with an icon and a text label, so colour is never the only signal,
and appear only in status columns.

App building blocks live in `web/src/styles/_app.scss` and are reused on every `/app` screen: page head
(breadcrumb, title, one meta line, actions), section and panel, the `.sr-dt` data table (aligned columns, mono
numbers right-aligned, whole-row links; below 640px — or 1024px with `.stack-md` — rows become stacked list rows),
list rows, labelled form fields with inline errors, notices, the `sr-stats` strip and `sr-state` for
loading/empty/error. Phones and tablets (below 960px) get a drawer for navigation and one compact menu for project,
persona and demo controls; touch targets are at least 44px; dialogs become full-screen sheets; the work-order board
shows one status column at a time below 1200px.

### Chart colours

The work-order status chart uses a categorical palette validated for lightness band, chroma, colour-vision
deficiency separation, normal-vision separation and contrast (Open `#A07AC0`, In progress `#2D6CC9`, Blocked
`#C53D33`, Completed `#BE8510`, Closed `#24804B`): the most muted set tried that passes every check. Every chart
also has a legend, direct counts, hover tooltips and a table view.

## Trade-offs

- **Monolith over services**: one free container; module boundaries keep the ERP adapter separable.
- **Same-origin cookies over JWT**: no tokens in browser storage, CSRF handled by antiforgery; no CORS.
- **String enums + CHECK constraints**: readable data and database-level guarantees, at a small storage cost.
- **Explicit audit writes over an interceptor**: summaries are meaningful ("Moved RBL-007 from In progress
  to Blocked") and only meaningful changes are recorded.
- **Not persisted data-protection keys**: a restart signs visitors out; acceptable for disposable demo
  workspaces (see known limitations in `STATUS.md`).
