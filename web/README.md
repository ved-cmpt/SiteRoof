# SiteRoof web

Angular frontend for SiteRoof. See the repository README for setup.

```powershell
npm ci
npm start        # http://localhost:4200, proxies /api to http://localhost:5080
npm run lint
npm test
npm run build    # dist/web/browser, served by the API in production
```

Icons are generated from `@material-symbols/svg-400` by `node scripts/gen-icons.mjs`.
