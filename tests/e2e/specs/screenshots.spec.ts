import { expect, test } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { startDemo, switchPersona } from './helpers';

// Captures real screenshots of the running app for the README and landing page.
// Run: npx playwright test --project=screenshots
const out = resolve(__dirname, '../../../docs/screenshots');
const landing = resolve(__dirname, '../../../web/public/screens');
mkdirSync(out, { recursive: true });
mkdirSync(landing, { recursive: true });

test.use({ viewport: { width: 1440, height: 900 } });

test('capture product screenshots', async ({ page }) => {
  test.setTimeout(180_000);
  await page.goto('/');
  await page.screenshot({ path: `${out}/landing.png` });

  await startDemo(page);
  await page.getByRole('button', { name: 'Close guide' }).click().catch(() => undefined);
  await expect(page.getByTestId('attention-queue')).toBeVisible();
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${landing}/overview.png` });
  await page.screenshot({ path: `${out}/overview.png` });

  await page.getByRole('link', { name: /Riverbend Library Renovation/ }).first().click();
  await expect(page.getByTestId('wo-RBL-007')).toBeVisible();
  await page.screenshot({ path: `${out}/project-board.png` });

  await page.getByRole('tab', { name: 'Dashboard' }).click();
  await expect(page.getByTestId('summary-label')).toBeVisible();
  await page.screenshot({ path: `${out}/project-dashboard.png`, fullPage: true });

  await page.getByRole('tab', { name: 'Work orders' }).click();
  await page.getByTestId('wo-RBL-007').click();
  await expect(page.getByTestId('progress-history')).toBeVisible();
  await page.screenshot({ path: `${landing}/work-order.png` });
  await page.screenshot({ path: `${out}/work-order.png`, fullPage: true });

  await page.goto('/app/approvals');
  await expect(page.getByTestId('approval-list')).toBeVisible();
  await expect(page.getByTestId('integration-panel')).toContainText('Recent transmissions');
  await page.screenshot({ path: `${landing}/approvals.png` });
  await page.screenshot({ path: `${out}/approvals.png` });

  await page.goto('/app/activity');
  await expect(page.getByTestId('activity-feed')).toBeVisible();
  await page.screenshot({ path: `${out}/activity.png` });

  await switchPersona(page, 'crew');
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/app/overview');
  await expect(page.getByTestId('kpi-overdue')).toBeVisible();
  await page.screenshot({ path: `${out}/mobile-overview.png` });
  await page.goto('/');
  await page.screenshot({ path: `${out}/mobile-landing.png` });

  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/case-study');
  await page.screenshot({ path: `${out}/case-study.png` });
});
