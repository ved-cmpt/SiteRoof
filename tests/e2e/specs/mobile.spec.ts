import { Page, expect, test } from '@playwright/test';
import { api, startDemo, switchPersona } from './helpers';

// Phone-sized checks (375 x 812): no horizontal overflow anywhere in /app, touch-sized controls,
// and the crew member's main job (logging progress and a blocker) done entirely on a phone.
test.use({ viewport: { width: 375, height: 812 }, hasTouch: true, isMobile: true, reducedMotion: 'reduce' });

async function expectNoHorizontalOverflow(page: Page, route: string): Promise<void> {
  const overflow = await page.evaluate(() => {
    const doc = document.documentElement;
    const content = document.querySelector('mat-sidenav-content');
    return {
      document: doc.scrollWidth - doc.clientWidth,
      content: content ? content.scrollWidth - content.clientWidth : 0,
    };
  });
  expect(overflow, `horizontal overflow on ${route}`).toEqual({ document: 0, content: 0 });
}

async function ids(page: Page) {
  const context = page.context();
  const projects = await (await api(context, page.request, 'GET', '/api/projects')).json();
  const rbl = projects.find((p: { code: string }) => p.code === 'RBL').id as string;
  const board = await (await api(context, page.request, 'GET', `/api/projects/${rbl}/work-orders?pageSize=100`)).json();
  const wo = (key: string) => board.items.find((w: { key: string }) => w.key === key).id as string;
  return { rbl, wo6: wo('RBL-006'), wo7: wo('RBL-007') };
}

test('no /app route scrolls horizontally on a phone', async ({ page }) => {
  test.setTimeout(120_000);
  await startDemo(page);
  const { rbl, wo7 } = await ids(page);
  const routes = [
    '/app/overview',
    '/app/projects',
    `/app/projects/${rbl}`,
    `/app/projects/${rbl}?tab=dashboard`,
    `/app/work-orders/${wo7}`,
    '/app/approvals',
    '/app/activity',
    '/app/admin',
    '/app/does-not-exist',
  ];
  for (const route of routes) {
    await page.goto(route);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await page.waitForLoadState('networkidle');
    await expectNoHorizontalOverflow(page, route);
  }

  // Milestones & team and List view stay within the screen too.
  await page.goto(`/app/projects/${rbl}`);
  await page.getByRole('tab', { name: 'Milestones & team' }).click();
  await expect(page.getByRole('heading', { name: 'Milestones' })).toBeVisible();
  await expectNoHorizontalOverflow(page, 'milestones & team');
  await page.getByRole('tab', { name: 'Work orders' }).click();
  await page.getByRole('button', { name: 'List' }).click();
  await expectNoHorizontalOverflow(page, 'work-order list');
});

test('the sidebar is a drawer and the switchers share one compact menu on a phone', async ({ page }) => {
  await startDemo(page);
  await expect(page.getByRole('navigation', { name: 'App' })).toBeHidden();
  await page.getByRole('button', { name: 'Navigation' }).click();
  const projects = page.getByRole('navigation', { name: 'App' }).getByRole('link', { name: 'Projects' });
  await expect(projects).toBeVisible();
  expect((await projects.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await projects.click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Projects');

  await page.getByTestId('persona-switcher').click();
  await expect(page.getByRole('menuitem', { name: /Riverbend Library Renovation/ })).toBeVisible();
  await expect(page.getByTestId('persona-crew')).toBeVisible();
  await page.getByRole('menuitem', { name: /Northgate Transit Shelter/ }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Northgate Transit Shelter');
});

test('the work-order board shows one status column at a time on a phone', async ({ page }) => {
  await startDemo(page);
  const { rbl } = await ids(page);
  await page.goto(`/app/projects/${rbl}`);
  const picker = page.getByRole('tablist', { name: 'Status column' });
  await expect(picker).toBeVisible();
  await picker.getByRole('tab', { name: /Blocked/ }).click();
  await expect(page.getByTestId('wo-RBL-007')).toBeVisible();
  await expect(page.getByTestId('wo-RBL-008')).toBeHidden();
  await picker.getByRole('tab', { name: /Open/ }).click();
  await expect(page.getByTestId('wo-RBL-008')).toBeVisible();
  await expect(page.getByTestId('wo-RBL-007')).toBeHidden();
});

test('a crew member logs progress and a blocker from a phone', async ({ page }) => {
  await startDemo(page);
  await switchPersona(page, 'crew');
  await page.goto('/app/overview');

  // Assigned work comes first for the crew and each row opens the work order.
  const mine = page.getByTestId('my-work');
  await expect(mine).toBeVisible();
  await mine.getByRole('link', { name: 'Drywall board — level 1 reading room' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Drywall board — level 1 reading room');

  const qty = page.getByTestId('progress-qty');
  await expect(qty).toBeVisible();
  expect((await qty.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  await qty.fill('25');
  await page.getByTestId('progress-blocker').fill('Out of joint compound until the delivery arrives.');

  const submit = page.getByTestId('progress-submit');
  const box = (await submit.boundingBox())!;
  expect(box.height).toBeGreaterThanOrEqual(44);
  expect(box.width).toBeGreaterThan(300);
  await submit.click();

  await expect(page.getByTestId('wo-status')).toContainText('Blocked');
  const latest = page.getByTestId('progress-history').locator('li').first();
  await expect(latest).toContainText('25 m²');
  await expect(latest).toContainText('Out of joint compound until the delivery arrives.');
  await expectNoHorizontalOverflow(page, 'work order after logging');
});

test('dialogs open as full-screen sheets on a phone', async ({ page }) => {
  await startDemo(page);
  await switchPersona(page, 'supervisor');
  const { rbl } = await ids(page);
  await page.goto(`/app/projects/${rbl}`);
  await page.getByTestId('new-work-order').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  const box = (await dialog.boundingBox())!;
  expect(box.width).toBeGreaterThanOrEqual(375);
  expect(box.height).toBeGreaterThanOrEqual(800);
  const create = page.getByTestId('wo-save');
  await expect(create).toBeInViewport();
  expect((await create.boundingBox())!.height).toBeGreaterThanOrEqual(44);
});
