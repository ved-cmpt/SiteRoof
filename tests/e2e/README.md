# End-to-end tests (Playwright)

Specs in `specs/` drive the real app in Chromium against a real SQL Server:

| Spec | Covers |
| --- | --- |
| `recruiter-demo.spec.ts` | Open Riverbend → inspect delayed work → crew submits progress → supervisor verifies → manager approves material request → ERP 503 then retry succeeds → dashboard and activity update |
| `security.spec.ts` | Crew changing a project id in the URL gets 403; crew cannot approve via UI or API; 404 page; CSRF required |
| `conflict.spec.ts` | Stale status change shows the 409 conflict banner and nothing is overwritten |
| `work-order-lifecycle.spec.ts` | Supervisor creates and assigns work; crew completes it; dashboard and activity feed update |
| `accessibility.spec.ts` | Skip link, single h1, landmarks, image alt text, mobile menu, labelled diagrams, top-bar project switcher |
| `screenshots.spec.ts` | Captures README screenshots and the landing-page figures in `web/public/screens` (separate `screenshots` project) |

```powershell
docker compose up -d db                 # SQL Server on 127.0.0.1:1434 (needs ../../.env)
cd ../../web; npm ci; npm run build     # the API serves this build
cd ../tests/e2e; npm ci; npx playwright install chromium
npx playwright test --project=chromium  # starts the API on http://127.0.0.1:5080 automatically
npx playwright test --project=screenshots
```

To test the production container or a hosted URL instead, set `E2E_BASE_URL`
(e.g. `http://127.0.0.1:8080` after `docker compose --profile full up -d`). Each spec provisions its own
demo workspace, so the per-client demo rate limit (6 per 15 minutes by default) must be raised for a full
run: `$env:DEMO_PROVISION_PERMITS=500; docker compose --profile full up -d`.
