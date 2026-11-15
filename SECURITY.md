# Security

SiteRoof is a personal project. It holds only fictional demo data, but it is built as if it
held real project records.

## Reporting a vulnerability

Please open a private security advisory on the GitHub repository (*Security* → *Report a vulnerability*)
rather than a public issue. Include steps to reproduce and the affected endpoint. There is no bounty.

## Threat model (short)

| Threat | Control |
| --- | --- |
| Reading another visitor's or project's data by changing ids | Tenant query filters on every tenant-owned entity (fail closed) **and** `ProjectAccess` object-level checks in every service call. Other tenants' ids → 404; same-tenant non-members → 403. Covered by xUnit and Playwright tests. |
| Privilege escalation (crew approving, supervisor reopening closed work) | Capabilities derived from the project membership role on the server; UI hiding is cosmetic. Tests cover crew approval attempts. |
| CSRF against the cookie-authenticated API | ASP.NET Core antiforgery: `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` header validated on every unsafe `/api` request; cookies are `SameSite`. Token re-issued whenever the identity changes. |
| Session theft via XSS | Auth cookie is HttpOnly and Secure when hosted; strict Content-Security-Policy (`script-src 'self'`, no inline scripts, no third-party origins); Angular's template escaping; all fonts and icons self-hosted. |
| Clickjacking / sniffing | `X-Frame-Options: DENY`, `frame-ancestors 'none'`, `X-Content-Type-Options: nosniff`, strict referrer policy, HSTS in production. |
| Injection | EF Core parameterized queries; the two raw SQL statements use interpolated parameters (`SqlQuery`/`FormattableString`). Inputs validated with data annotations and business rules; database CHECK constraints as a backstop. |
| Abuse of the public demo | Per-client rate limits (API, demo provisioning, sign-in), a cap on active demo tenants, per-tenant quotas, automatic expiry and cleanup. |
| Brute-force sign-in | Identity lockout after 5 failures, rate-limited endpoint, generic error messages. Demo personas have no password at all. |
| Duplicate or replayed business actions | Material requests are submitted once (client idempotency key, unique index) and decided once; ERP messages carry idempotency keys and the receiver de-duplicates. |
| Lost updates | `rowversion` optimistic concurrency with a visible 409 conflict. |
| Mock ERP endpoint exposed in the same container | Requires a shared key (`Erp__ApiKey`, random per process if unset), lives outside `/api`, rejects calls without `Idempotency-Key`. |
| Secret leakage | No secrets in the repository: `.env` is git-ignored, only `.env.example` is tracked; configuration comes from environment variables / the host's secret store. History scanned with gitleaks. |
| Information disclosure in errors | ProblemDetails with generic 500 messages; exception details only in server logs, correlated by `traceId`. |

## Operational notes

- Structured JSON logs in production (one line per API request with status, latency, user id, trace id).
- Health checks: `/health` (liveness) and `/health/ready` (database).
- Data-protection keys are not persisted (a restart signs users out). Persist them (e.g. to the database or
  blob storage) before using SiteRoof with real accounts.
- Optional AI integration is disabled by default; if enabled it only receives already-authorized,
  minimal context and its output is evidence-filtered and labelled. It can never approve or change records.

## Known gaps

- No multi-factor authentication or account self-service (out of scope for the demo).
- No automated dependency vulnerability gate beyond Dependabot.
- Rate limiting is per instance (in memory); multiple instances would need a distributed limiter.
