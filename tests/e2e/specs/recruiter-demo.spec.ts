import { expect, test } from '@playwright/test';
import { startDemo, switchPersona } from './helpers';

// The scripted recruiter scenario from the spec:
// Open Riverbend -> inspect delayed work -> submit progress -> approve material request -> see dashboard and ERP status update.
test('recruiter demo scenario runs end to end on real data', async ({ page }) => {
  test.setTimeout(120_000);
  await startDemo(page);
  await expect(page.getByTestId('kpi-approvals')).toHaveText('5');
  await expect(page.getByTestId('guide-panel')).toBeVisible();

  // 1. Open Riverbend and inspect delayed work (as the site supervisor).
  await switchPersona(page, 'supervisor');
  await page.getByRole('link', { name: 'Projects', exact: true }).click();
  await page.getByTestId('project-RBL').click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Riverbend Library Renovation');
  await page.getByRole('switch', { name: 'Overdue only' }).click();
  await expect(page.getByTestId('wo-RBL-007')).toBeVisible();
  await expect(page.getByTestId('wo-RBL-006')).toBeVisible();
  await expect(page.getByTestId('wo-RBL-012')).toHaveCount(0);
  await page.getByTestId('wo-RBL-007').click();
  await expect(page.getByText('Short 40 sheets of 5/8" Type X board')).toBeVisible();

  // 2. Submit progress as the crew member on RBL-006.
  await page.getByRole('link', { name: 'RBL · Riverbend Library Renovation' }).click();
  await switchPersona(page, 'crew');
  await page.getByTestId('wo-RBL-006').click();
  await page.getByTestId('progress-qty').fill('35');
  await page.getByTestId('progress-note').fill('South wall finished, bulkhead returns started.');
  await page.getByTestId('progress-submit').click();
  const latest = page.getByTestId('progress-history').locator('li').first();
  await expect(latest).toContainText('35 m²');
  await expect(latest).toContainText('Awaiting verification');

  // 3. Supervisor verifies it.
  await switchPersona(page, 'supervisor');
  await latest.getByTestId('verify-progress').click();
  await expect(latest).toContainText('Verified by Marcus Oyelaran');

  // 4. Project manager approves the drywall board request.
  await switchPersona(page, 'manager');
  await page.getByRole('link', { name: 'Approvals', exact: true }).click();
  await page.getByTestId('mr-RBL-007').getByTestId('approve').click();
  await page.getByTestId('decision-note').fill('Order from the yard; the Tuesday truck is too late.');
  await page.getByTestId('decision-confirm').click();
  await expect(page.getByText(/Approved\. The requisition is queued/)).toBeVisible();

  // 5. The ERP simulator rejects attempt 1 with 503; the worker retries and the status updates without a reload.
  await page.getByTestId('filter-approved').click();
  const approved = page.getByTestId('mr-RBL-007');
  await expect(approved.getByTestId('erp-Sent')).toBeVisible({ timeout: 45_000 });
  await expect(approved).toContainText('after 2 attempts');
  await expect(approved.getByTestId('erp-reference')).toHaveText(/^PR-\d{6}-[0-9A-F]{6}$/);

  // 6. Dashboards are recalculated from the database.
  await page.getByRole('link', { name: 'Home', exact: true }).click();
  await expect(page.getByTestId('kpi-approvals')).toHaveText('4');
  await page.getByRole('link', { name: 'Activity', exact: true }).click();
  const feed = page.getByTestId('activity-feed');
  await expect(feed).toContainText('Approved 40 sheets');
  await expect(feed).toContainText('for RBL-007');
  await expect(feed).toContainText('ERP acknowledged requisition');
  await expect(feed).toContainText('reported on RBL-006');

  // The guided scenario's checkmarks come from the stored records.
  await expect(page.getByTestId('guide-toggle')).toContainText('4/6');
});
