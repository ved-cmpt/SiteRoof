# SiteRoof — Status

_Last updated: 2026-11-15 (Phase 1 in progress)_

## Done

- Repository scaffolding, local commit hooks, spec (`docs/PROJECT_SPEC.md`), `AGENTS.md`.
- API: domain model, EF Core migrations (all entities, FKs, check constraints, unique keys, indexes),
  T-SQL views `vw_ProjectWorkOrderRollup` / `vw_WorkPackageCompletion` used by dashboards.
- Identity cookie auth, CSRF validation, tenant query filters, project-membership authorization.
- Guest demo provisioning (isolated tenant per visitor), persona switcher, reset, expiry cleanup.
- Projects, milestones, work orders (transitions, comments, dependencies, concurrency), progress
  logs, verification, material requests + approvals, outbox + mock ERP with retries, activity, dashboards,
  rules-based blocker summary.
- Backend tests: `dotnet test tests/SiteRoof.Api.Tests` → 59 passed, 0 failed (Testcontainers SQL Server 2022).

## In progress / next

- Angular app (landing, case study, app shell and screens).
- Playwright, Docker, CI, docs.

## How to resume

Read `AGENTS.md`, then continue with the first unchecked item above.
