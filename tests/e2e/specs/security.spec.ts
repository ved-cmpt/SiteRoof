import { expect, test } from '@playwright/test';
import { api, projectId, startDemo, switchPersona } from './helpers';

test('a crew member cannot view another project by changing the URL id', async ({ page, context }) => {
  await startDemo(page);
  const northgate = await projectId(context, page.request, 'NTS');
  await switchPersona(page, 'crew');

  await page.goto('/app/projects');
  await expect(page.getByTestId('project-RBL')).toBeVisible();
  await expect(page.getByTestId('project-NTS')).toHaveCount(0);

  await page.goto(`/app/projects/${northgate}`);
  await expect(page.getByTestId('forbidden-page')).toBeVisible();
  await expect(page.getByText('Northgate Transit Shelter')).toHaveCount(0);

  const res = await api(context, page.request, 'GET', `/api/projects/${northgate}/work-orders`);
  expect(res.status()).toBe(403);
  expect((await res.json()).code).toBe('forbidden');

  await page.goto('/app/admin');
  await expect(page.getByTestId('forbidden-page')).toBeVisible();
});

test('crew cannot approve material requests even by calling the API', async ({ page, context }) => {
  await startDemo(page);
  await switchPersona(page, 'crew');
  const list = await (await api(context, page.request, 'GET', '/api/material-requests?status=Submitted')).json();
  const id = list.items[0].id as string;
  await page.goto('/app/approvals');
  await expect(page.getByTestId('approval-list')).toBeVisible();
  await expect(page.getByTestId('approve')).toHaveCount(0);
  const res = await api(context, page.request, 'POST', `/api/material-requests/${id}/decision`, { decision: 'Approve' });
  expect(res.status()).toBe(403);
});

test('unknown routes show the 404 page and the API refuses writes without a CSRF token', async ({ page, context }) => {
  await startDemo(page);
  await page.goto('/app/does-not-exist');
  await expect(page.getByTestId('not-found-page')).toBeVisible();
  const rbl = await projectId(context, page.request, 'RBL');
  const res = await page.request.post(`/api/projects/${rbl}/milestones`, { data: { name: 'No token', dueDate: '2027-01-01' } });
  expect(res.status()).toBe(400);
  expect((await res.json()).code).toBe('csrf-invalid');
});
