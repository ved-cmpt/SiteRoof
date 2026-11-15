# AGENTS.md — working conventions for SiteRoof

Read `docs/PROJECT_SPEC.md` (scope and rules) and `docs/STATUS.md` (what is done, what is next)
before changing anything. Preserve working code; reconcile, don't rewrite.

## Layout

| Path | What |
| --- | --- |
| `api/SiteRoof.Api` | ASP.NET Core (.NET 10) modular monolith: `Domain/`, `Data/` (EF Core, migrations, T-SQL views in `Data/Sql`), `Auth/`, `Common/`, `Features/<Module>/`, `Infrastructure/` |
| `web` | Angular (standalone components, strict TS, SCSS, Angular Material) |
| `tests/SiteRoof.Api.Tests` | xUnit unit + integration tests (Testcontainers SQL Server) |
| `tests/e2e` | Playwright end-to-end tests |
| `scripts` | PowerShell helpers for local development |
| `docs` | Spec, status, architecture, database, API, deployment |

## Commands

```powershell
docker compose up -d db                 # SQL Server 2022 on 127.0.0.1:1434
./scripts/dev-api.ps1                   # API on http://localhost:5080 (migrates on startup)
./scripts/dev-web.ps1                   # Angular on http://localhost:4200 (proxies /api)
dotnet test                             # backend tests (needs Docker for Testcontainers)
cd web; npm run lint; npm test; npm run build
cd tests/e2e; npx playwright test       # see tests/e2e/README.md
docker compose --profile full up -d --build   # single container on http://localhost:8080
```

Migrations: `dotnet ef migrations add <Name> --project api/SiteRoof.Api --output-dir Data/Migrations`
(tool restored via `dotnet tool restore`).

## Ports

SQL Server 1434 (host), API 5080, Angular dev 4200, local container 8080. Do not change them; other
projects on this machine use the defaults.

## Conventions

- Every read and write of project data goes through `ProjectAccess` (tenant + membership) in the service
  layer. Never rely on the UI to hide actions. Cross-tenant ids must 404; same-tenant non-members 403.
- Tenant-owned entities carry EF global query filters; system code (workers, provisioning, cleanup) uses
  `IgnoreQueryFilters()` explicitly and scopes by tenant id itself.
- Business changes and their `AuditEvent` (and outbox rows) are saved in one `SaveChanges` call so they
  commit atomically.
- Errors are ProblemDetails with a stable `code` extension (`forbidden`, `concurrency-conflict`,
  `work-order-locked`, `dependencies-incomplete`, ...). The Angular client keys messages off `code`.
- Optimistic concurrency: work orders and material requests use `rowversion`; clients send `version`.
- Dates that drive "overdue" use Mountain time (`SiteClock`, and `AT TIME ZONE 'Mountain Standard Time'` in views).
- All seeded people, companies, addresses and records are fictional. No real company branding.
- Conventional commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`, `ci:`), small and logical.

## Hard rules

- Never commit secrets. Only `.env.example` is tracked; `.env` is ignored.
- Never mention tooling vendors or assistants in tracked files, commit messages, branch names or PRs, and
  never add co-author trailers. Local git hooks (`.git/hooks/commit-msg`, `pre-commit`) enforce this.
- Local-only notes (`.local/`, editor/assistant folders) stay untracked via `.git/info/exclude`.
- No paid services, no cloud provisioning, no deployment, no `git push` without the owner.
- Ask before destructive commands (dropping volumes/databases, deleting branches, rewriting history).
- Never fabricate test results, metrics or screenshots; record real command output in `docs/STATUS.md`.
