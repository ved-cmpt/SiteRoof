# SiteRoof API

The machine-readable contract is the OpenAPI 3.1 document served at **`/openapi/v1.json`** (all
environments). In Development an interactive reference is available at `/scalar`.

## Conventions

- JSON over HTTPS, same origin as the web app. Enums are strings (`"InProgress"`, `"Approve"`).
- **Authentication**: ASP.NET Core Identity cookie `siteroof.auth` (HttpOnly, SameSite=Lax, Secure when hosted).
  Unauthenticated calls return `401`; the API never redirects.
- **CSRF**: `GET /api/auth/me` (and every sign-in) sets a readable `XSRF-TOKEN` cookie. Every `POST`,
  `PATCH` and `DELETE` under `/api` must echo it in the `X-XSRF-TOKEN` header, otherwise `400 csrf-invalid`.
  Angular's `HttpClient` does this automatically for same-origin requests.
- **Errors**: RFC 9457 ProblemDetails with a stable `code` and a `traceId`:

  ```json
  { "status": 409, "title": "Conflict", "code": "concurrency-conflict",
    "detail": "This work order was changed by someone else since you opened it. Reload to see the latest version, then try again.",
    "currentVersion": "AAAAAAAAB9E=", "currentStatus": "Blocked", "traceId": "00-…" }
  ```

- **Pagination**: list endpoints accept `page` (1-based) and `pageSize` (max 100) and return
  `{ items, page, pageSize, totalCount, totalPages }`.
- **Concurrency**: work orders return a `version` (base64 `rowversion`). `PATCH /api/work-orders/{id}` and
  `POST …/transitions` require it; a stale value returns `409 concurrency-conflict`.

### Error codes

| Status | `code` | Meaning |
| --- | --- | --- |
| 400 | `validation` | Field validation failed (`errors` holds per-field messages) |
| 400 | `csrf-invalid` | Missing or wrong `X-XSRF-TOKEN` |
| 401 | — | Not signed in, or the demo workspace expired |
| 403 | `forbidden` | Signed in but not allowed (role or project membership) |
| 404 | `not-found` | Unknown id, or an id that belongs to another workspace |
| 409 | `concurrency-conflict` | Stale `version` |
| 409 | `work-order-locked` | Closed or cancelled work order |
| 409 | `invalid-transition` | Status change not valid from the current state |
| 409 | `already-decided`, `already-verified`, `already-resolved`, `duplicate-dependency`, `duplicate-code`, `already-member`, `not-failed` | Repeated or contradictory action |
| 422 | `dependencies-incomplete`, `close-preconditions`, `assignee-required`, `self-verification`, `dependency-cycle`, `project-completed`, `work-order-completed`, `work-order-not-active`, `no-blocker`, `quota-exceeded`, `demo-capacity` | Business rule |
| 429 | `rate-limited` | Too many requests (`Retry-After` header) |

## Endpoints

### Session and demo

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/api/auth/me` | Current session; issues the CSRF cookie |
| POST | `/api/auth/login` | `{ email, password }` for named accounts (rate-limited, lockout after 5 failures) |
| POST | `/api/auth/logout` | |
| POST | `/api/demo/start` | `{ persona: "manager" \| "supervisor" \| "crew" }` → provisions a private workspace (rate-limited per client) |
| GET | `/api/demo/scenario` | Personas and guided steps; `done` flags are computed from stored records |
| POST | `/api/demo/persona` | Switch persona inside the same demo workspace |
| POST | `/api/demo/reset` | Replace the caller's demo workspace with a fresh one |

### Projects, milestones, people

| Method | Route | Who |
| --- | --- | --- |
| GET | `/api/projects?status=&search=` | Members (admins: all workspace projects) |
| POST | `/api/projects` | Tenant role ProjectManager or Admin |
| GET | `/api/projects/{id}` | Members (detail, roster, milestones, permissions) |
| PATCH | `/api/projects/{id}` | Project manager |
| POST | `/api/projects/{id}/members` | Project manager — `{ userId, role, trade }` |
| GET/POST | `/api/projects/{id}/milestones` | Members / project manager |
| PATCH | `/api/milestones/{id}` | Project manager |
| GET | `/api/users` | Signed-in users (own workspace only) |

### Work orders and progress

| Method | Route | Who |
| --- | --- | --- |
| GET | `/api/projects/{id}/work-orders?status=&assigneeId=&workPackage=&priority=&overdue=&mine=&search=&sort=number\|due\|priority` | Members |
| POST | `/api/projects/{id}/work-orders` | Manager or supervisor; assignee must be on the roster |
| GET | `/api/work-orders/mine` | Signed-in user's active assignments |
| GET | `/api/work-orders/{id}` | Members; includes `availableTransitions` and `permissions` for the caller |
| PATCH | `/api/work-orders/{id}` | Manager or supervisor; `version` required |
| POST | `/api/work-orders/{id}/transitions` | `{ transition: Start\|Block\|Unblock\|Complete\|Reopen\|Close\|Cancel, version, reason? }` |
| GET/POST | `/api/work-orders/{id}/comments` | Members |
| POST | `/api/work-orders/{id}/dependencies` | Manager or supervisor — `{ dependsOnWorkOrderId }` |
| DELETE | `/api/work-orders/{id}/dependencies/{dependsOnId}` | Manager or supervisor |
| GET/POST | `/api/work-orders/{id}/progress` | Assignee or supervisor — `{ quantity ≥ 0, blocker?, note? }` |
| POST | `/api/progress/{id}/verify` | Manager or supervisor, not the reporter |
| POST | `/api/progress/{id}/resolve-blocker` | Manager or supervisor |

### Materials and integrations

| Method | Route | Who |
| --- | --- | --- |
| GET | `/api/material-requests?status=&projectId=` | Members of the related projects |
| GET | `/api/work-orders/{id}/material-requests` | Members |
| POST | `/api/work-orders/{id}/material-requests` | Assignee or supervisor — `{ clientRequestId, itemName, quantity > 0, unit, justification? }`; 201 created, 200 for a repeated `clientRequestId` |
| POST | `/api/material-requests/{id}/decision` | Project manager — `{ decision: Approve\|Reject, note?, version? }`; reject needs a note; repeat = no-op |
| GET | `/api/integrations/health` | Outbox counts, recent transmissions, worker heartbeat, mock ERP reachability |
| POST | `/api/integrations/outbox/{id}/retry` | Project manager; only for `Failed` events |

### Dashboards, insights, activity, admin

| Method | Route | Notes |
| --- | --- | --- |
| GET | `/api/overview` | KPIs, per-project status counts, attention queue, my work |
| GET | `/api/projects/{id}/dashboard` | KPIs, status breakdown, completion by work package, overdue work, open blockers, approval backlog, milestones, recent activity |
| GET | `/api/projects/{id}/blocker-summary` | `generator: "rules"`, `label: "Rules-based summary"`, evidence-linked points |
| GET | `/api/activity?projectId=&entityType=&action=&entityId=` | Audit feed with `before` / `after` JSON |
| GET | `/api/projects/{id}/activity` | Same, scoped to one project |
| GET/PATCH | `/api/admin/tenant` | Tenant Admin role — `{ name?, erpSimulatorMode?: Normal\|FailFirstAttempt\|Unavailable }` |

### Operational

| Route | Notes |
| --- | --- |
| `GET /health` | Liveness (no dependencies) |
| `GET /health/ready` | Readiness (database reachable) |
| `POST /mock-erp/requisitions` | Self-hosted mock ERP. Requires `X-Mock-Erp-Key` and `Idempotency-Key`; not part of the public contract |

## Example: approve a material request

```http
POST /api/material-requests/6cb43dc2-…/decision
Cookie: siteroof.auth=…; siteroof.af=…; XSRF-TOKEN=…
X-XSRF-TOKEN: CfDJ8…
Content-Type: application/json

{ "decision": "Approve", "note": "Order from the yard." }
```

```json
{
  "id": "6cb43dc2-…", "workOrderKey": "RBL-007", "itemName": "5/8\" Type X gypsum board 4×8",
  "quantity": 40, "unit": "sheets", "status": "Approved",
  "erp": { "status": "Queued", "attempts": 0, "nextRetryAt": "2026-11-15T08:02:11Z", "externalReference": null }
}
```

A few seconds later `GET /api/material-requests?status=Approved` shows `erp.status` move through
`Retrying` (simulated 503) to `Sent` with an `externalReference` such as `PR-261115-1A2B3C`.
