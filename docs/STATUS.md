# SiteRoof — Status

_Last updated: 2026-11-15. Phases 1, 2 and 3 plus the visual redesign are complete locally. **Not deployed.**_

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

## Verification (run on 2026-11-15 after the redesign, Windows 11, Docker Desktop)

| Command | Result |
| --- | --- |
| `dotnet build SiteRoof.slnx -c Release -warnaserror` | Succeeded, 0 warnings, 0 errors |
| `dotnet test SiteRoof.slnx` | **60 passed**, 0 failed (xUnit; SQL Server 2022 via Testcontainers) |
| `cd web; npm run lint` | All files pass linting |
| `cd web; npm test` | **14 passed** (3 files, Vitest) |
| `cd web; npm run build` | Succeeded, no warnings; initial bundle 323.74 kB raw / 86.46 kB estimated transfer |
| `cd tests/e2e; npx playwright test --project=chromium` (API on :5080 serving the production build) | **10 passed** |
| `docker compose --profile full up -d --build` then `E2E_BASE_URL=http://127.0.0.1:8080 npx playwright test --project=chromium` (with `DEMO_PROVISION_PERMITS=500`) | **10 passed** against the rebuilt production container |
| `docker run rhysd/actionlint:1.7.12` on `.github/workflows` | No findings |
| `docker run zricethezav/gitleaks:v8.30.1 git /repo` | No leaks found |

The `actionlint` and `gitleaks` rows above are from the pre-redesign run; the redesign did not touch the workflow.

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
- The board uses horizontal scrolling for its five status columns on small screens; List view is the
  better mobile layout.
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
