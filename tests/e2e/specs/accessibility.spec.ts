import { expect, test } from '@playwright/test';
import { startDemo, switchPersona } from './helpers';

test('landing page is keyboard reachable with a skip link and labelled images', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Skip to content' })).toBeFocused();
  for (const img of await page.locator('img').all()) {
    expect(await img.getAttribute('alt')).toBeTruthy();
  }
  await page.getByTestId('hero-try-demo').focus();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/app\/home/);
});

test('app pages expose one h1, landmarks and a working mobile menu', async ({ page }) => {
  await startDemo(page);
  for (const path of ['/app/home', '/app/projects', '/app/approvals', '/app/activity', '/app/admin']) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toHaveCount(1);
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'App' })).toBeAttached();
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await page.getByRole('button', { name: 'Menu' }).click();
  await expect(page.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Case study' })).toBeVisible();
});

test('case study renders its diagrams with accessible names', async ({ page }) => {
  await page.goto('/case-study');
  await expect(page.getByRole('img', { name: 'Runtime architecture' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Data model' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'Deployment pipeline' })).toBeVisible();
  await expect(page.getByText('A personal project.').first()).toBeVisible();
});

test('sidebar project list shows only accessible projects and marks the current one', async ({ page }) => {
  await startDemo(page);
  const list = page.getByTestId('sidebar-projects');
  await expect(list.getByRole('link')).toHaveCount(3); // the manager is on all three projects
  await list.getByRole('link', { name: /NTS\s*Northgate Transit Shelter/ }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Northgate Transit Shelter');
  await expect(list.getByRole('link', { name: /Northgate Transit Shelter/ })).toHaveAttribute('aria-current', 'page');
  await expect(list.locator('[aria-current="page"]')).toHaveCount(1);

  // The crew member is only on Riverbend, so the sidebar lists only Riverbend.
  await switchPersona(page, 'crew');
  await expect(list.getByRole('link')).toHaveCount(1);
  await expect(list.getByRole('link')).toContainText('Riverbend Library Renovation');
});

test('the persona switcher lives at the bottom of the sidebar and switches persona', async ({ page }) => {
  await startDemo(page);
  const sidebar = page.locator('#app-nav');
  const switcher = sidebar.getByTestId('persona-switcher');
  await expect(switcher).toBeVisible();
  await expect(switcher).toContainText('Dana Whitford');
  await expect(page.getByTestId('persona-switcher')).toHaveCount(1);

  // It sits below the navigation and the project list, with the guide entry directly above it.
  const nav = (await sidebar.getByRole('navigation', { name: 'App' }).boundingBox())!;
  const projects = (await sidebar.getByTestId('sidebar-projects').boundingBox())!;
  const guide = (await sidebar.getByTestId('guide-toggle').boundingBox())!;
  const profile = (await switcher.boundingBox())!;
  expect(profile.y).toBeGreaterThan(nav.y + nav.height);
  expect(profile.y).toBeGreaterThan(projects.y + projects.height);
  expect(guide.y + guide.height).toBeLessThanOrEqual(profile.y + 1);
  expect(profile.y + profile.height).toBeGreaterThan(900 - 80);

  await switcher.click();
  await expect(page.getByTestId('reset-demo')).toBeVisible();
  await page.getByTestId('persona-supervisor').click();
  await expect(page.getByText(/You are now/)).toBeVisible();
  await expect(switcher).toContainText('Marcus Oyelaran');
  await expect(switcher).toContainText('Site Supervisor');
});
