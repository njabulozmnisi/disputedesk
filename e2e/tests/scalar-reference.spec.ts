import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { expect, test, type Page } from '@playwright/test';
import { fixtureLogin } from './support';

// Story 5.5: Scalar at /scalar/ drives the operations API with a pasted bearer token, behind Scalar:Enabled. In a real
// browser through the gateway. The page is anonymous; the token comes from scripts/operations-token.sh and is pasted by hand.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const repoRoot = new URL('../..', import.meta.url).pathname;
const scalarCsp =
  "default-src 'self'; script-src 'self' 'sha256-HASH'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
const nonCspHeaders = ['x-content-type-options', 'x-frame-options', 'referrer-policy', 'permissions-policy', 'cross-origin-opener-policy'];

test('the Scalar page headers: the Scalar CSP with one script hash that matches the inline script, every other header equal to those on /', async ({ request }) => {
  const page = await request.get('/scalar/');
  expect(page.status()).toBe(200);
  const html = await page.text();
  const inline = [...html.matchAll(/<script(?![^>]*\ssrc=)[^>]*>([\s\S]*?)<\/script>/g)];
  expect(inline).toHaveLength(1);
  const hash = createHash('sha256').update(inline[0][1]).digest('base64');
  expect(page.headers()['content-security-policy']).toBe(scalarCsp.replace('HASH', hash));
  const root = await request.get('/');
  for (const name of nonCspHeaders) expect(page.headers()[name], name).toBe(root.headers()[name]);
  for (const path of ['/scalar/scalar.js', '/openapi/v1.json']) {
    expect((await request.get(path)).headers()['content-security-policy'], path).toBe(scalarCsp.replace('HASH', hash));
  }
  // the page holds no configured value (issuer, project ID, secrets)
  for (const forbidden of ['auth.localhost', 'clientSecret', 'clientId', 'tokenUrl', 'proxyUrl', 'persistAuth']) expect(html).not.toContain(forbidden);
  expect(html).toContain('"showDeveloperTools":"never"');
});

test('the gateway routes: /scalar redirects, files are served, other /openapi/ paths fall to the SPA', async ({ request }) => {
  const redirect = await request.get('/scalar', { maxRedirects: 0 });
  expect(redirect.status()).toBe(302);
  expect(redirect.headers()['location']).toMatch(/\/scalar\/$|^scalar\/$/);
  for (const path of ['/scalar/scalar.js', '/scalar/scalar.aspnetcore.js', '/scalar/favicon.svg', '/openapi/v1.json']) {
    expect((await request.get(path)).status(), path).toBe(200);
  }
  for (const path of ['/openapi/v2.json', '/openapi/']) {
    const response = await request.get(path);
    expect(response.headers()['content-type'], path).toContain('text/html');
    expect(response.headers()['content-security-policy'], path).toContain("script-src 'self';");
  }
});

test('the runtime document declares one bearer scheme, required on the operations route only', async ({ request }) => {
  const document = await (await request.get('/openapi/v1.json')).json();
  expect(document.components.securitySchemes).toEqual({ bearer: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' } });
  for (const [path, item] of Object.entries<Record<string, { security?: unknown }>>(document.paths)) {
    for (const operation of Object.values(item)) {
      if (path.startsWith('/api/v1/operations/')) expect(operation.security, path).toEqual([{ bearer: [] }]);
      else expect(operation.security, path).toBeUndefined();
    }
  }
  expect(JSON.stringify(document)).not.toMatch(/tokenUrl|clientSecret|clientId|oauth2|auth\.localhost/);
});

async function openScalarWithRecorder(page: Page) {
  const problems: string[] = [];
  page.on('console', (message) => {
    if (/Content Security Policy|Refused to/i.test(message.text())) problems.push(`console: ${message.text().slice(0, 200)}`);
  });
  await page.addInitScript(() => {
    (window as unknown as { __csp: string[] }).__csp = [];
    document.addEventListener('securitypolicyviolation', (event) => {
      (window as unknown as { __csp: string[] }).__csp.push(`${event.violatedDirective} blocked=${event.blockedURI} sample=${event.sample}`);
    });
  });
  await page.goto('/scalar/');
  return problems;
}

test('Scalar loads, a pasted operations token reads a dispute, the only CSP violation is the zod eval probe, nothing is persisted', async ({ page, browser }) => {
  // a dispute to read: A raises through the API
  const token = execFileSync(`${repoRoot}scripts/operations-token.sh`, { encoding: 'utf8' }).trim();
  const context = await browser.newContext();
  const login = fixtureLogin('customer-a');
  const customerPage = await context.newPage();
  await customerPage.goto('/api/v1/bff/login');
  await customerPage.getByTestId('username-text-input').fill(login.username);
  await customerPage.getByTestId('username-text-input').press('Enter');
  await customerPage.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await customerPage.getByTestId('password-text-input').press('Enter');
  await customerPage.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  const list = await (await context.request.get('/api/v1/transactions?limit=50')).json();
  const row = (list.items as { transactionId: string; accountRef: string; disputable: boolean }[]).find((item) => item.disputable && item.accountRef === 'ACC-A-1');
  expect(row, 'a disputable ACC-A-1 transaction is left').toBeTruthy();
  const csrf = (await (await context.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const raised = await context.request.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': csrf, 'Idempotency-Key': `"qa5x-scalar-${Date.now()}"` },
    data: { accountRef: 'ACC-A-1', transactionId: row!.transactionId, reason: 'unrecognised' },
    maxRedirects: 0,
  });
  expect(raised.status()).toBe(201);
  const disputeId = raised.headers().location.split('/').pop()!;
  await context.close();

  const problems = await openScalarWithRecorder(page);
  await expect(page.getByText('Operations', { exact: false }).first()).toBeVisible({ timeout: 30_000 });

  // open the operation in the API client, open Authentication and paste the token into the Bearer Token field
  await page.getByRole('button', { name: 'Open Group - Operations' }).click();
  await page.getByRole('link', { name: /disputeId/ }).last().click();
  await page.getByRole('button', { name: /Test Request.*operations/ }).click();
  await page.getByRole('button', { name: /Authentication|Auth Type/i }).last().click();
  await expect(page.getByText('Selected Auth Type: bearer')).toBeVisible();
  const requests: { url: string; hasAuthorization: boolean }[] = [];
  page.on('request', (request) => requests.push({ url: request.url(), hasAuthorization: 'authorization' in request.headers() }));
  await page.getByPlaceholder('Token').fill(token);

  const pathValue = page.locator('[aria-label=" Value"]').first();
  await pathValue.click();
  await page.keyboard.type(disputeId);
  const responded = page.waitForResponse((response) => response.url().includes(`/api/v1/operations/disputes/${disputeId}`));
  await page.getByRole('button', { name: /^Send/ }).first().click();
  const response = await responded;
  expect(response.status()).toBe(200);
  expect(Object.keys(await response.json()).sort()).toEqual(
    ['createdAt', 'disputeId', 'disputedAmount', 'expectedAmount', 'outcome', 'reason', 'status', 'timeline', 'transaction']);
  await expect(page.getByText('"disputeId"').first()).toBeVisible({ timeout: 30_000 });
  expect(requests.some((entry) => entry.url.includes(`/api/v1/operations/disputes/${disputeId}`) && entry.hasAuthorization)).toBe(true);

  const violations = await page.evaluate(() => (window as unknown as { __csp: string[] }).__csp);
  console.log('CSP violations recorded:', JSON.stringify([...problems, ...violations]));
  expect(violations.filter((entry) => !entry.startsWith("script-src") || !entry.includes('eval'))).toEqual([]);

  const storage = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }));
  expect(storage).not.toContain(token);
  expect(storage).not.toContain(token.split('.')[2]);
  console.log('storage keys after the read:', Object.keys(JSON.parse(storage).local).join(','));
});

test('another page path under /scalar/ fails closed: its inline script is refused by the CSP and the page stays blank', async ({ page }) => {
  await page.addInitScript(() => {
    (window as unknown as { __csp: string[] }).__csp = [];
    document.addEventListener('securitypolicyviolation', (event) => (window as unknown as { __csp: string[] }).__csp.push(event.violatedDirective));
  });
  await page.goto('/scalar/v1');
  await page.waitForTimeout(2000);
  const violations = await page.evaluate(() => (window as unknown as { __csp: string[] }).__csp);
  expect(violations.some((directive) => directive.startsWith('script-src'))).toBe(true);
  await expect(page.getByRole('button', { name: 'Open Group - Operations' })).toHaveCount(0);
});
