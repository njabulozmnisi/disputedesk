import { expect, test } from '@playwright/test';
import { fixtureLogin } from './support';

// Story 5.5 criterion 1, switch off: with Scalar__Enabled=false (docker compose -f docker-compose.yml -f e2e/scalar-off.compose.yml
// up -d --wait --no-deps api, then run with SCALAR_OFF=1) the Scalar routes and the runtime document are not mapped:
// 401 anonymous (the fallback policy), 404 signed in through the gateway, never the SPA's index.html.
test.skip(process.env.SCALAR_OFF !== '1', 'runs only against an api started with Scalar__Enabled=false');

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const paths = ['/scalar', '/scalar/', '/scalar/scalar.js', '/scalar/scalar.aspnetcore.js', '/scalar/favicon.svg', '/openapi/v1.json'];

test('anonymous gets 401 and a signed-in customer gets 404 on every Scalar route and the runtime document', async ({ browser, request }) => {
  for (const path of paths) {
    const response = await request.get(path, { maxRedirects: 0 });
    expect(response.status(), `anonymous ${path}`).toBe(401);
    expect(response.headers()['content-type'], path).toContain('application/problem+json');
  }

  const context = await browser.newContext();
  const page = await context.newPage();
  const login = fixtureLogin('customer-a');
  await page.goto('/api/v1/bff/login');
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  for (const path of paths) {
    const response = await context.request.get(path, { maxRedirects: 0 });
    expect(response.status(), `signed in ${path}`).toBe(404);
    expect(response.headers()['content-type'], path).not.toContain('text/html');
  }
  await context.close();
});
