import { APIRequestContext, BrowserContext, Page, expect } from '@playwright/test';

export type Persona = 'manager' | 'supervisor' | 'crew';

/** Starts a fresh demo workspace from the landing page, exactly as a visitor would. */
export async function startDemo(page: Page): Promise<void> {
  await page.goto('/');
  await page.getByTestId('hero-try-demo').click();
  await expect(page).toHaveURL(/\/app\/overview/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Overview');
}

/** Switches persona through the in-app switcher. */
export async function switchPersona(page: Page, persona: Persona): Promise<void> {
  await page.getByTestId('persona-switcher').click();
  await page.getByTestId(`persona-${persona}`).click();
  await expect(page.getByText(/You are now/)).toBeVisible();
}

/** API helper using the page's cookies, sending the CSRF header like the Angular client. */
export async function api(context: BrowserContext, request: APIRequestContext, method: 'GET' | 'POST' | 'PATCH', url: string, data?: unknown) {
  const cookies = await context.cookies();
  const xsrf = cookies.find((c) => c.name === 'XSRF-TOKEN')?.value ?? '';
  const res = await request.fetch(url, { method, data, headers: { 'X-XSRF-TOKEN': xsrf } });
  return res;
}

export async function projectId(context: BrowserContext, request: APIRequestContext, code: string): Promise<string> {
  const res = await api(context, request, 'GET', '/api/projects');
  expect(res.ok()).toBeTruthy();
  const projects = (await res.json()) as { id: string; code: string }[];
  return projects.find((p) => p.code === code)!.id;
}
