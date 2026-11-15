# SiteRoof — Status

_Last updated: 2026-11-15. Phases 1–3, the visual redesign and the app polish pass are complete locally. **Not deployed.**_

## Done

**Phase 1 — foundation and vertical slice**
- EF Core migrations for every entity (Tenant, User/Identity, Project, ProjectMembership, Milestone, WorkOrder
  with `rowversion`, comments, dependencies, ProgressLog, MaterialRequest, IntegrationOutbox, AuditEvent, mock ERP
  tables) with FKs, CHECK constraints (non-negative quantities, statuses, dates), unique keys and indexes.
- T-SQL views `vw_ProjectWorkOrderRollup` and `vw_WorkPackageCompletion`, used by the projects list,
  overview and project dashboard (documented in `docs/DATABASE.md`).
- Identity cookie auth, antiforgery on every unsafe `/api` call, roles (Admin, ProjectManager, SiteSupervisor,
  CrewMember), tenant query filters + project-membership checks on every read and write.
- "Try Demo": per-visitor tenant, password-less personas, persona switcher (manager/supervisor/crew of that
  tenant only), rate-limited provisioning, capacity cap.
- Idempotent seed: Riverbend Library Renovation (behind on drywall), Northgate Transit Shelter, Maplewood
  Community Centre (completed); 30 work orders, 11 milestones, overdue items, 5 pending + 1 rejected material
  requests, 1 ERP retry outbox event.
- REST API with OpenAPI (`/openapi/v1.json`), ProblemDetails with stable codes, validation, pagination, filters.
- Angular: landing page, case study, app shell, overview (KPIs, project switcher, status chart, attention queue),
  projects, project detail (board/list, dashboard, milestones & team, activity), work-order detail, 403/404.

**Phase 2 — domain workflows**
- Supervisor creates/assigns → crew reports progress and blockers → supervisor verifies/resolves → material request →
  manager approves/rejects once (audited) → transactional outbox → background worker → self-hosted mock ERP
  (503 on first attempt, bounded exponential backoff, idempotency key, external reference stored).
- `/app/approvals` with pending/approved/rejected, live ERP transmission status and integration health;
  `/api/integrations/health`; manual retry for failed transmissions.
- Comments, dependencies (cycle detection), closure rules, locked closed work, manager reopen with reason,
  optimistic concurrency with a visible 409 conflict banner.
- Activity feed + audit trail with before/after JSON at `/app/activity` and per project.
- Dashboards: overdue work, completion by work package, open blockers, approval backlog, recent activity.
- Accessibility and polish: skip links, labelled controls, visible focus, reduced motion, landmarks, responsive
  layouts (390 px to desktop), charts with legend + table view, validated chart palette.

**Phase 3 — hardening and presentation**
- Rules-based open-blocker summary with evidence links, labelled "Rules-based summary"; `IBlockerSummaryProvider`
  extension point for an optional AI provider (disabled, not implemented) with evidence filtering and fallback.
- Demo lifecycle: Reset My Demo (shell menu, banner and Admin page), 8-hour expiry, background cleanup,
  per-tenant quotas, rate limits.
- Security: rate limits (API, demo provisioning, sign-in), validation, CSP and security headers, HSTS in production,
  structured JSON logs, `/health` and `/health/ready`, secrets only from environment variables, `SECURITY.md`.
- Multi-stage `Dockerfile` (single container), `docker-compose.yml` (db + full profile), GitHub Actions CI
  (backend, frontend, Playwright, Docker build, required `ci-ok`, docs-only skip), Dependabot (monthly, grouped).
- Docs: README with real screenshots, `ARCHITECTURE.md`, `DATABASE.md`, `API.md`, `DEPLOYMENT.md` (NOT DEPLOYED),
  final `/case-study` with SVG architecture, data model and deployment diagrams, MIT `LICENSE`.

**Visual redesign (2026-11-15)**
- One design system for the whole app ("a calm product UI printed on a construction drawing"): tokens in
  `web/src/styles/_tokens.scss`, Angular Material re-skinned in `_material.scss`, shared primitives in `_base.scss`.
  Warm drawing-paper background, hairlines, one construction-orange accent, Inter + JetBrains Mono (self-hosted),
  muted status pills with icons and labels, re-validated chart palette. Details in `docs/ARCHITECTURE.md`.
- Landing page rebuilt as a short drawing set: hero with a real screenshot, an annotated work-order screenshot with
  numbered callouts, a schedule-style capability table, an ERP split section, a verification strip with recorded
  numbers only, and a title-block footer. Case study, sign-in, demo start and 403/404 restyled to match.
- App shell: light sidebar with line icons and an accent marker, compact top bar with a new project switcher and
  the persona switcher, quieter demo strip. Overview, projects, board, dashboard, work order, approvals, activity
  and admin restyled (hairline KPI strips, mono ids/dates/quantities, hairline board cards).
- New browser test for the project switcher. A Playwright run exposed that the switcher's accessible name included
  option text; fixed with an explicit `aria-label` (no test assertion was relaxed).
- Screenshots in `docs/screenshots` and `web/public/screens` recaptured from the redesigned app.

**App polish pass (2026-11-15)**
- Audited every /app screen, dialog and state at 375, 768 and 1440 px (51 captures, untracked in `.local/audit/`),
  listed 34 issues and fixed each; resolutions are recorded next to the list.
- Type: IBM Plex Sans + IBM Plex Mono (self-hosted). App scale of three sizes (20/14/12 px), weights 400/500 with
  600 for page titles only; spacing scale 4–32 px; one 6px radius.
- Removed dashboard tells: greeting, six KPI tiles (now one 4-number strip), pills outside status columns, heading
  icons, card grids and filler sentences. Overview, projects, approvals, milestones, team and admin are tables.
- Phones and tablets: drawer navigation and one compact menu, tables stack into list rows, one board column at a
  time with a status picker, 44px touch targets, full-screen sheet dialogs, sticky submit for logging progress,
  crew sees "Assigned to me" first.
- Bugs found and fixed during the pass: the guided-demo panel opened over the content on phones; tab paging arrows
  overlapped tabs on phones; the tablet approvals table clipped its action column; desktop dialogs scrolled
  sideways; the compact menu header ran name and role together.
- New Playwright phone checks (375 px): no horizontal overflow on every /app route (plus milestones/team and list
  view), drawer and compact menu, one-column board, crew logs progress and a blocker, dialogs as full-screen sheets.

## Verification (run on 2026-11-15 after the polish pass, Windows 11, Docker Desktop)

| Command | Result |
| --- | --- |
| `cd web; npm run lint` | All files pass linting |
| `cd web; npm test` | **14 passed** (3 files, Vitest) |
| `cd web; npm run build` | Succeeded, no warnings; initial bundle 340.36 kB raw / 88.41 kB estimated transfer |
| `dotnet test SiteRoof.slnx` | **60 passed**, 0 failed (xUnit; SQL Server 2022 via Testcontainers) |
| `cd tests/e2e; npx playwright test --project=chromium` (API on :5080 serving the production build) | **15 passed** (10 desktop + 5 phone-viewport) |
| `docker compose --profile full up -d --build` (default demo limit, 6 per client per 15 min) | Built and started; `/health/ready` 200; demo start, `/api/overview` and `/app/overview` return 200 |
| `node scripts/audit.mjs .local/audit/after http://127.0.0.1:8080` | 51 after-screenshots captured from the production container |

The full Playwright suite was not run against the default-limit container: it starts about 15 demo workspaces and
the production limit allows 6 per client per 15 minutes (see Known issues). It passed against the production build
served by the API. The previous container run (raised limit) passed 10/10 before the polish pass.

Earlier checks still valid (no backend or workflow changes since): `dotnet build -c Release -warnaserror` 0 warnings,
actionlint no findings, gitleaks no leaks.

Local latency (pre-redesign; backend unchanged since), measured with `node scripts/measure-latency.mjs http://127.0.0.1:8080 30` against the
production container and SQL Server 2022 in Docker on the development machine (not a hosted environment):

| Request | Median | p95 |
| --- | --- | --- |
| `POST /api/demo/start` (provision tenant + seed, n=3) | 184 ms | 185 ms |
| `GET /api/overview` | 16 ms | 28 ms |
| `GET /api/projects/{id}/dashboard` | 33 ms | 49 ms |
| `GET /api/projects/{id}/work-orders` | 16 ms | 25 ms |
| `GET /api/projects/{id}/blocker-summary` | 16 ms | 20 ms |

These are single-machine numbers for orientation only; nothing has been measured on a hosted environment.

## Known issues and limitations

- **Not deployed.** CI has only been validated locally (actionlint); it runs for real once the GitHub repository exists.
- Data-protection keys are not persisted: a container restart signs visitors out (they start a new demo).
- Rate limiting is in memory, per instance.
- Dependabot's `open-pull-requests-limit: 2` applies per ecosystem (GitHub has no global cap).
- Running the full Playwright suite against the container or a hosted URL needs a higher demo provisioning
  limit (`DEMO_PROVISION_PERMITS` / `Demo__ProvisionPermitsPerWindow`), because each spec creates a workspace.
- Material form fields in the work-order, project and material dialogs keep their own outline style; all other
  app forms use the shared labelled fields.
- The optional AI summary provider is an interface only; no provider is implemented or configured.

## Owner-only next steps

1. Create the GitHub repository `SiteRoof` under the owner's account, add the remote, push `main`
   (`docs/DEPLOYMENT.md` §1) and require the `ci-ok` check on `main`.
2. Create the Azure SQL Database free-offer database and firewall rules (§2).
3. Create the Render free web service from the Dockerfile and set the secrets
   `ConnectionStrings__SiteRoof` and `Erp__ApiKey` (§3).
4. Run the smoke test in §4, record the URL and results here, and update the README "Hosting status" line.
5. Optional: confirm the GitHub URL used on the landing page and case study (`https://github.com/ved-cmpt/SiteRoof`).

## How to resume

Read `AGENTS.md` and this file. Start the stack with `docker compose up -d db` and `./scripts/dev-api.ps1`, then
run the verification commands above before changing anything.
