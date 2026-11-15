import { expect, test } from '@playwright/test';
import { startDemo } from './helpers';

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
  await expect(page).toHaveURL(/\/app\/overview/);
});

test('app pages expose one h1, landmarks and a working mobile menu', async ({ page }) => {
  await startDemo(page);
  for (const path of ['/app/overview', '/app/projects', '/app/approvals', '/app/activity', '/app/admin']) {
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
  await expect(page.getByText('Independent portfolio project.').first()).toBeVisible();
});

test('top-bar project switcher lists only accessible projects and navigates', async ({ page }) => {
  await startDemo(page);
  const switcher = page.getByRole('combobox', { name: 'Switch project' });
  await expect(switcher.locator('option')).toHaveCount(4); // "All projects" + three projects for the manager
  await switcher.selectOption({ label: 'NTS · Northgate Transit Shelter' });
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Northgate Transit Shelter');
  await expect(switcher).toHaveValue(/[0-9a-f-]{36}/);
});
