# Deployment guide (owner steps)

> **Status: NOT DEPLOYED.** Nothing has been provisioned. These are the exact steps for the owner to host
> SiteRoof for free: a **Render free web service** built from the repository `Dockerfile`, and an **Azure SQL
> Database free offer** database. Both require the owner's own accounts; no credentials are stored in the repo.

## What gets deployed

One container (`Dockerfile`): ASP.NET Core API + the built Angular app on port 8080 (or `$PORT`),
applying EF Core migrations on startup. It needs one secret: the SQL connection string.

## 1. GitHub repository

```powershell
# On github.com create an empty repository named SiteRoof (no README/license), then:
git remote add origin https://github.com/ved-cmpt/SiteRoof.git
git push -u origin main
```

In the repository settings → Branches, add a protection rule for `main` that requires the **`ci-ok`**
check. CI runs on every push and pull request (`.github/workflows/ci.yml`).

## 2. Azure SQL Database (free offer)

1. Azure portal → **SQL databases** → **Create**.
2. Create a new logical server: choose a region close to the Render region (e.g. *Canada Central* or
   *West US 2*), **SQL authentication**, an admin login and a strong password (store it in a password manager).
3. Database name `siteroof`. On the *Basics* tab select **Apply offer** for the *free* Azure SQL Database
   offer (serverless General Purpose; 100,000 vCore seconds and 32 GB per month). Choose **"Auto-pause the
   database until next month"** when the free limit is reached so it never bills.
4. *Networking*: **Public endpoint**. Add firewall rules for Render's outbound IP addresses (Render dashboard →
   your service → *Connect* → *Outbound*), and your own IP if you want to connect with a SQL client.
   Do **not** open 0.0.0.0–255.255.255.255.
5. After creation, copy the ADO.NET connection string and build:

   ```
   Server=tcp:<server>.database.windows.net,1433;Initial Catalog=siteroof;User ID=<admin-login>;Password=<password>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;
   ```

   The app creates the schema and views itself on first start (`Database__MigrateOnStartup=true`). For least
   privilege later: run migrations once with the admin login (`dotnet run --project api/SiteRoof.Api -- --migrate-only`
   with the connection string set locally), then create a contained database user with only `db_datareader` and
   `db_datawriter`, use it in the app's connection string and set `Database__MigrateOnStartup=false`.

Notes: the serverless database auto-pauses when idle; the first request afterwards can take up to a minute while
it resumes. The API uses EF Core's retry-on-failure for transient errors.

## 3. Render web service (free)

1. render.com → **New** → **Web Service** → connect the GitHub repository.
2. **Runtime**: Docker (Render detects the `Dockerfile`). **Instance type**: Free. Branch: `main`.
3. **Health check path**: `/health` (liveness; does not depend on the database waking up).
4. **Environment variables** (Environment tab; mark secrets as secret):

   | Key | Value |
   | --- | --- |
   | `ConnectionStrings__SiteRoof` | the Azure SQL connection string from step 2 (**secret**) |
   | `PORT` | `8080` |
   | `Erp__ApiKey` | a random 48-character string (**secret**), e.g. from `openssl rand -hex 24` |
   | `ASPNETCORE_ENVIRONMENT` | `Production` (already the image default) |
   | `Demo__MaxActiveTenants` | optional, default `300` |
   | `Demo__ProvisionPermitsPerWindow` | optional, default `6` per client per 15 minutes |

   Do not set `Security__SecureCookies=false` in a hosted environment.
5. Create the service. The first build takes several minutes. Render terminates TLS and forwards
   `X-Forwarded-*` headers, which the app honours (`ForwardedHeaders__Enabled=true` in Production).

Free-tier behaviour: the service sleeps after about 15 minutes without traffic and wakes on the next request
(cold start up to about a minute). Background work (ERP retries, demo cleanup) resumes when it wakes. Demo
sessions do not survive a restart because data-protection keys are not persisted; visitors simply start a new demo.

## 4. Smoke test (required before claiming it is live)

```powershell
$base = "https://<service>.onrender.com"
curl.exe -s "$base/health"            # 200 Healthy
curl.exe -s "$base/health/ready"      # 200 Healthy (database reachable)
curl.exe -sI "$base/"                 # 200 with Content-Security-Policy header
# Full browser scenario against the hosted site (uses ~9 demo workspaces; raise
# Demo__ProvisionPermitsPerWindow temporarily or run specs individually):
cd tests/e2e; $env:E2E_BASE_URL = $base; npx playwright test --project=chromium
```

Record the date, URL and results in `docs/STATUS.md`, then update the README "Live demo" line and the
GitHub link on the landing page/case study if the repository URL differs.

## 5. Rollback and operations

- Render keeps previous deploys: *Events* → select a deploy → **Rollback**.
- Logs are structured JSON (Render *Logs* tab). Each API request logs method, path, status, elapsed ms,
  user id and trace id; errors include the `traceId` returned in ProblemDetails.
- To wipe all guest data: guest tenants expire automatically after 8 hours. There is no shared demo data to reset.

## Alternatives (not configured)

The same container runs on Azure App Service for Containers or Azure Container Apps; set the same environment
variables there. A separate static host for the Angular app is unnecessary because the API serves it from the
same origin, which is what keeps cookie auth simple and CORS unused.
