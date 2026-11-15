// Design audit capture: screenshots of every /app screen, dialog and state at phone, tablet and desktop sizes.
// Usage (from tests/e2e): node scripts/audit.mjs <outDir> [baseUrl]
// Each viewport provisions one demo workspace, so the per-client demo limit must allow 3 starts.
import { chromium } from '@playwright/test';
import { mkdirSync } from 'node:fs';

const out = process.argv[2];
const base = process.argv[3] ?? 'http://127.0.0.1:8080';
if (!out) throw new Error('Usage: node scripts/audit.mjs <outDir> [baseUrl]');
mkdirSync(out, { recursive: true });

const viewports = [
  ['375', { width: 375, height: 812 }],
  ['768', { width: 768, height: 1024 }],
  ['1440', { width: 1440, height: 900 }],
];

const browser = await chromium.launch();
for (const [tag, viewport] of viewports) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
  const page = await context.newPage();
  const shot = async (name, opts = {}) => {
    await page.waitForTimeout(350);
    await page.screenshot({ path: `${out}/${tag}-${name}.png`, fullPage: opts.full ?? false });
  };
  const go = async (path) => {
    await page.goto(base + path, { waitUntil: 'networkidle' });
  };
  const persona = async (key) => {
    await page.getByTestId('persona-switcher').click();
    await page.getByTestId(`persona-${key}`).click();
    await page.getByText(/You are now/).waitFor();
    await page.keyboard.press('Escape');
  };
  const api = (path) => page.evaluate(async (p) => (await fetch(p)).json(), path);

  await go('/demo');
  await page.waitForURL(/app\/overview/);
  await page.waitForLoadState('networkidle');
  const close = page.getByRole('button', { name: 'Close guide' });
  if (await close.isVisible()) await close.click();

  const projects = await api('/api/projects');
  const rbl = projects.find((p) => p.code === 'RBL').id;
  const nts = projects.find((p) => p.code === 'NTS').id;
  const board = await api(`/api/projects/${rbl}/work-orders?pageSize=100`);
  const wo7 = board.items.find((w) => w.key === 'RBL-007').id;
  const wo6 = board.items.find((w) => w.key === 'RBL-006').id;

  await go('/app/overview');
  await shot('01-overview-manager', { full: true });
  await page.getByTestId('persona-switcher').click();
  await shot('02-persona-menu');
  await page.keyboard.press('Escape');
  await go('/app/projects');
  await shot('03-projects', { full: true });
  await go(`/app/projects/${rbl}`);
  await shot('04-project-board');
  await go(`/app/projects/${rbl}?tab=dashboard`);
  await shot('05-project-dashboard', { full: true });
  await go(`/app/work-orders/${wo7}`);
  await shot('06-work-order', { full: true });
  await go('/app/approvals');
  await shot('07-approvals', { full: true });
  await page.getByTestId('approve').first().click();
  await shot('08-dialog-decision');
  await page.keyboard.press('Escape');
  await go('/app/activity');
  await shot('09-activity');
  await go('/app/admin');
  await shot('10-admin', { full: true });
  await go('/app/projects/00000000-0000-0000-0000-000000000000');
  await shot('11-error-404');

  await persona('supervisor');
  await go(`/app/projects/${rbl}`);
  await page.getByTestId('new-work-order').click();
  await shot('12-dialog-new-work-order');
  await page.keyboard.press('Escape');
  await go(`/app/projects/${rbl}`);
  const search = page.getByLabel('Search', { exact: false }).first();
  await search.fill('zzzz-no-match');
  await search.press('Enter');
  await shot('13-empty-board');

  await persona('crew');
  await go('/app/overview');
  await shot('14-overview-crew', { full: true });
  await go(`/app/work-orders/${wo6}`);
  await shot('15-work-order-crew', { full: true });
  await page.getByTestId('request-materials').click();
  await shot('16-dialog-material');
  await page.keyboard.press('Escape');
  await go(`/app/projects/${nts}`);
  await shot('17-error-403');
  await context.close();
}
await browser.close();
console.log(`Saved screenshots to ${out}`);
