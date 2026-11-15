# SiteRoof — Status

_Last updated: 2026-11-15 — Phase 1 and Phase 2 complete; Phase 3 in progress._

## Done

- Phase 1: schema + migrations for every entity, T-SQL dashboard views, Identity cookie auth with CSRF,
  tenant query filters and project-membership authorization, per-visitor demo tenants with persona
  switcher, idempotent seed, REST API, Angular landing / case study / app shell / overview / projects /
  work-order screens.
- Phase 2: full supervisor → crew → manager journey, material request approval state machine,
  transactional outbox + background worker + self-hosted mock ERP (503 once, bounded backoff, idempotency
  keys), approvals screen with live ERP status and integration health, comments, dependencies, closure
  rules, 409 conflict banner, activity/audit feed with before/after diff, project dashboards,
  accessibility pass (skip links, labels, focus, reduced motion, responsive layouts).
- Tests (all run locally on 2026-11-15):
  - `dotnet test tests/SiteRoof.Api.Tests` → 59 passed, 0 failed.
  - `cd web; npm test` → 14 passed; `npm run lint` → all files pass; `npm run build` → success.
  - `cd tests/e2e; npx playwright test --project=chromium` → 9 passed.

## Next (Phase 3)

Dockerfile, CI, Dependabot, SECURITY.md, deployment guide, README with screenshots, architecture /
database / API docs, license, final verification.

## How to resume

Read `AGENTS.md`, then continue with the "Next" list above.
