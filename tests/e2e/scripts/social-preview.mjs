// Renders web/public/social-preview.png (1200 x 630) from HTML, using the self-hosted fonts and the
// Home screenshot captured by specs/screenshots.spec.ts. Run from tests/e2e: node scripts/social-preview.mjs
import { chromium } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
const font = (pkg, file) =>
  readFileSync(resolve(root, `web/node_modules/@fontsource/${pkg}/files/${file}`)).toString('base64');
const shot = readFileSync(resolve(root, 'web/public/screens/home.png')).toString('base64');

const logo = (size) => `
<svg width="${size}" height="${size}" viewBox="0 0 24 24" aria-hidden="true">
  <path fill="#16181d" d="M1.5 4h21l-5.6 7.3H7.1z"/>
  <path fill="#16181d" d="M1.5 20h21l-5.6-7.3H7.1z"/>
  <path fill="#16181d" d="M1.5 5.6 5.9 12l-4.4 6.4z"/>
  <path fill="#d9480f" d="M22.5 5.6 18.1 12l4.4 6.4z"/>
</svg>`;

const html = `<!doctype html><html><head><style>
@font-face { font-family: Plex; font-weight: 400; src: url(data:font/woff2;base64,${font('ibm-plex-sans', 'ibm-plex-sans-latin-400-normal.woff2')}) format('woff2'); }
@font-face { font-family: Plex; font-weight: 600; src: url(data:font/woff2;base64,${font('ibm-plex-sans', 'ibm-plex-sans-latin-600-normal.woff2')}) format('woff2'); }
@font-face { font-family: PlexMono; font-weight: 400; src: url(data:font/woff2;base64,${font('ibm-plex-mono', 'ibm-plex-mono-latin-400-normal.woff2')}) format('woff2'); }
* { box-sizing: border-box; margin: 0; }
html, body { width: 1200px; height: 630px; overflow: hidden; }
body {
  font-family: Plex, sans-serif; color: #16181d; background-color: #f6f4ef;
  background-image: linear-gradient(rgb(22 24 29 / 4.5%) 1px, transparent 1px), linear-gradient(90deg, rgb(22 24 29 / 4.5%) 1px, transparent 1px);
  background-size: 24px 24px; position: relative;
}
.text { position: absolute; left: 72px; top: 64px; width: 500px; height: 502px; display: flex; flex-direction: column; }
.brand { display: flex; align-items: center; gap: 14px; font-weight: 600; font-size: 30px; letter-spacing: -0.01em; }
.label { margin-top: 64px; font-family: PlexMono, monospace; font-size: 16px; letter-spacing: 0.08em; text-transform: uppercase; color: #67625a; }
h1 { margin-top: 18px; font-size: 54px; line-height: 1.06; font-weight: 600; letter-spacing: -0.025em; }
.foot { margin-top: auto; font-family: PlexMono, monospace; font-size: 14px; white-space: nowrap; color: #3d4048; border-top: 1px solid #cfc9bd; padding-top: 18px; }
.shot { position: absolute; left: 630px; top: 92px; width: 760px; border: 1px solid #cfc9bd; border-radius: 6px; overflow: hidden;
  background: #fff; box-shadow: 0 1px 2px rgb(22 24 29 / 6%), 0 12px 32px rgb(22 24 29 / 8%); }
.shot img { display: block; width: 100%; }
</style></head><body>
  <div class="text">
    <div class="brand">${logo(36)}<span>SiteRoof</span></div>
    <p class="label">Construction operations</p>
    <h1>Keep site work, decisions, and progress in one place.</h1>
    <p class="foot">Work orders · field progress · approvals · ERP handoff</p>
  </div>
  <div class="shot"><img src="data:image/png;base64,${shot}" alt=""></div>
</body></html>`;

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 } });
await page.setContent(html, { waitUntil: 'load' });
await page.evaluate(() => document.fonts.ready);
await page.screenshot({ path: resolve(root, 'web/public/social-preview.png') });
await browser.close();
console.log('wrote web/public/social-preview.png');
