import { expect, test } from '@playwright/test';
import { api, startDemo, switchPersona } from './helpers';

test('a stale edit shows a visible 409 conflict instead of overwriting', async ({ page, context }) => {
  await startDemo(page);
  await switchPersona(page, 'supervisor');
  await page.getByRole('link', { name: 'Projects', exact: true }).click();
  await page.getByTestId('project-RBL').click();
  await page.getByTestId('wo-RBL-006').click();
  await expect(page.getByTestId('wo-status')).toContainText('In progress');
  const id = page.url().split('/').pop()!;

  // Someone else blocks the work order while this page still holds the old version.
  const current = await (await api(context, page.request, 'GET', `/api/work-orders/${id}`)).json();
  const other = await api(context, page.request, 'POST', `/api/work-orders/${id}/transitions`, {
    transition: 'Block',
    version: current.version,
    reason: 'Changed from another device',
  });
  expect(other.ok()).toBeTruthy();

  await page.getByTestId('transition-Complete').click();
  await expect(page.getByTestId('conflict-banner')).toBeVisible();
  await expect(page.getByTestId('conflict-banner')).toContainText('Someone else changed this work order');
  await expect(page.getByTestId('wo-status')).toContainText('In progress');

  await page.getByTestId('conflict-reload').click();
  await expect(page.getByTestId('wo-status')).toContainText('Blocked');
  await expect(page.getByTestId('conflict-banner')).toHaveCount(0);
});
