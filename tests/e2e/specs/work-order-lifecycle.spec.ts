import { expect, test } from '@playwright/test';
import { api, projectId, startDemo, switchPersona } from './helpers';

test('creating and completing a work order updates the dashboard and activity feed', async ({ page, context }) => {
  await startDemo(page);
  await switchPersona(page, 'supervisor');
  const rbl = await projectId(context, page.request, 'RBL');
  const before = await (await api(context, page.request, 'GET', `/api/projects/${rbl}/dashboard`)).json();

  await page.goto(`/app/projects/${rbl}`);
  await page.getByTestId('new-work-order').click();
  await page.getByTestId('wo-title').fill('Patch drywall at return air grilles');
  await page.getByTestId('wo-package').fill('Drywall');
  await page.getByTestId('wo-assignee').click();
  await page.getByRole('option', { name: /Leah Tremblay/ }).click();
  await page.getByLabel('Planned quantity').fill('12');
  await page.getByLabel('Unit').fill('m2');
  await page.getByTestId('wo-save').click();

  await expect(page).toHaveURL(/\/app\/work-orders\//);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Patch drywall at return air grilles');
  await expect(page.getByTestId('wo-assignee')).toHaveText('Leah Tremblay');
  await expect(page.getByTestId('wo-status')).toContainText('Open');

  await switchPersona(page, 'crew');
  await page.getByTestId('progress-qty').fill('12');
  await page.getByTestId('progress-submit').click();
  await expect(page.getByTestId('wo-status')).toContainText('In progress');
  await page.getByTestId('transition-Complete').click();
  await expect(page.getByTestId('wo-status')).toContainText('Completed');

  const after = await (await api(context, page.request, 'GET', `/api/projects/${rbl}/dashboard`)).json();
  expect(after.kpis.total).toBe(before.kpis.total + 1);
  expect(after.kpis.done).toBe(before.kpis.done + 1);

  await page.goto(`/app/projects/${rbl}?tab=dashboard`);
  await expect(page.getByTestId('dash-percent')).toHaveText(`${after.kpis.percentComplete}%`);

  await page.getByRole('link', { name: 'Activity', exact: true }).click();
  const feed = page.getByTestId('activity-feed');
  await expect(feed).toContainText('Created RBL-016 · Patch drywall at return air grilles');
  await expect(feed).toContainText('Moved RBL-016 from In progress to Completed');
});
