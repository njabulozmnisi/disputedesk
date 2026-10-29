import { execFileSync } from 'node:child_process';
import { expect, test, type Page } from '@playwright/test';
import {
  antiforgeryCookieName, expectProblemShape, fixtureLogin, queryDatabase, sessionCookieName,
} from './support';

// Story 2.8: sign-in and sign-out are audited, and every endpoint is denied by default.
// Four hops: browser (Zitadel), network (gateway), database (qa_readonly) and the API logs. One browser, serial.
// Nothing here prints a cookie, token, email or subject: assertions compare, they do not echo.
test.describe.configure({ mode: 'serial' });

const origin = 'http://localhost:8080';
const customerA = { ...fixtureLogin('customer-a'), ...fixtureFull('customer-a') };
const customerC = { ...fixtureLogin('customer-c'), ...fixtureFull('customer-c') };
const startedAt = new Date().toISOString();

function fixtureFull(key: string): { userId: string; email: string } {
  const text = execFileSync('cat', [new URL('../../fixtures/local.json', import.meta.url).pathname], { encoding: 'utf8' });
  const login = JSON.parse(text).logins.find((entry: { key: string }) => entry.key === key);
  return { userId: login.userId, email: login.email };
}

const auditCount = () => Number(queryDatabase('select count(*) from "AuditLogEntries"'));
const sessionCount = () => Number(queryDatabase('select count(*) from "Sessions"'));

/** The audit rows written after the first `skip` rows, oldest first, as `Action|Result|ActorType|ActorId|CustomerId|TraceId`. */
function auditRowsAfter(skip: number): string[] {
  const sql = `select concat_ws('|', "Action", "Result", "ActorType", "ActorId", coalesce("CustomerId", 'null'), coalesce("TraceId", 'null'))
    from "AuditLogEntries" order by "Id" offset ${skip}`;
  return queryDatabase(sql).split('\n').filter(Boolean);
}

async function signIn(page: Page, login: { username: string; password: string }, loginUrl = '/api/v1/bff/login') {
  await page.goto(loginUrl);
  // Zitadel keeps its own session after a sign-in, so a second login completes silently, without the form.
  if (new URL(page.url()).origin === origin) return;
  const account = page.getByRole('button', { name: new RegExp(login.username) });
  const username = page.getByTestId('username-text-input');
  const password = page.getByTestId('password-text-input');
  await expect(account.or(username).or(password)).toBeVisible();
  if (await account.isVisible()) await account.click();
  if (await username.isVisible()) {
    await username.fill(login.username);
    await username.press('Enter');
  }
  await password.fill(login.password);
  await password.press('Enter');
  await page.waitForURL((url) => url.origin === origin);
}

function apiLogsSinceStart(): string {
  // docker logs writes the container's stderr to stderr, so capture both streams.
  return execFileSync('sh', ['-c', `docker logs --since ${startedAt} disputedesk-api-1 2>&1`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

let page: Page;

test.beforeAll(async ({ browser }) => {
  page = await (await browser.newContext()).newPage();
});

test.afterAll(async () => {
  await page.context().close();
});

test('anonymous: unknown route, transactions and health (criteria 7 to 9)', async ({ request }) => {
  const unknown = await request.get('/api/v1/no-such-route', { maxRedirects: 0 });
  expect(unknown.status()).toBe(401);
  expect(unknown.headers()['content-type']).toContain('application/problem+json');
  expect(unknown.headers()['cache-control']).toBe('no-store');
  expect(expectProblemShape(await unknown.json(), 'unauthorized', 401)).toEqual([]);

  const transactions = await request.get('/api/v1/transactions', { maxRedirects: 0 });
  expect(transactions.status()).toBe(401);
  expect(expectProblemShape(await transactions.json(), 'unauthorized', 401)).toEqual([]);

  // The gateway serves the API's health endpoints under /api/health/*; /health/* is the SPA fallback.
  for (const path of ['/api/health/live', '/api/health/ready']) {
    const health = await request.get(path, { maxRedirects: 0 });
    expect(health.status()).toBe(200);
    expect(health.headers()['content-type']).toContain('text/plain');
  }
});

test('sign-in writes exactly one SignIn audit row for a linked login, with no token or email in the API logs', async () => {
  const auditBefore = auditCount();
  const sessionsBefore = sessionCount();

  await signIn(page, customerA);
  expect(new URL(page.url()).pathname).toBe('/');
  expect(await (await page.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'linked' });

  expect(auditCount()).toBe(auditBefore + 1);
  expect(sessionCount()).toBe(sessionsBefore + 1);
  const [row] = auditRowsAfter(auditBefore);
  const [action, result, actorType, actorId, customerId, traceId] = row.split('|');
  expect([action, result, actorType]).toEqual(['SignIn', 'Succeeded', 'Customer']);
  expect(actorId).toBe(customerA.userId);
  expect(customerId).toBe(queryDatabase(`select "CustomerId" from "CustomerLinks" where "LoginSub" = '${customerA.userId}'`));
  expect(traceId).toMatch(/^[0-9a-f]{32}$/);
  expect(queryDatabase(`select "LoginSub" from "Sessions" order by "AbsoluteExpiresAtUtc" desc limit 1`)).toBe(customerA.userId);
});

test('a signed-in unknown route is 404 not-found with a traceId; transactions are 200', async () => {
  const unknown = await page.request.get('/api/v1/no-such-route', { maxRedirects: 0 });
  expect(unknown.status()).toBe(404);
  expect(unknown.headers()['cache-control']).toBe('no-store');
  expect(expectProblemShape(await unknown.json(), 'not-found', 404)).toEqual([]);
  expect((await page.request.get('/api/v1/transactions?limit=1')).status()).toBe(200);
});

test('a second sign-in while signed in writes SignOut for the ended session, then SignIn', async () => {
  const auditBefore = auditCount();
  const sessionsBefore = sessionCount();

  await signIn(page, customerA);
  expect(await (await page.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'linked' });

  const rows = auditRowsAfter(auditBefore).map((row) => row.split('|').slice(0, 5).join('|'));
  const customerId = queryDatabase(`select "CustomerId" from "CustomerLinks" where "LoginSub" = '${customerA.userId}'`);
  expect(rows).toEqual([
    `SignOut|Succeeded|Customer|${customerA.userId}|${customerId}`,
    `SignIn|Succeeded|Customer|${customerA.userId}|${customerId}`,
  ]);
  // The ended session's row is gone and the new one is in: the count is unchanged.
  expect(sessionCount()).toBe(sessionsBefore);
});

let oldSessionCookie = '';

test('sign-out: row deleted, both cookies cleared, SignOut audited, end_session with its four parameters, lands on / signed out', async ({ browser }) => {
  await page.request.get('/api/v1/bff/antiforgery');
  const cookies = await page.context().cookies(origin);
  expect(cookies.map((cookie) => cookie.name).sort()).toEqual([antiforgeryCookieName, sessionCookieName]);
  oldSessionCookie = cookies.find((cookie) => cookie.name === sessionCookieName)!.value;

  const { requestToken } = await (await page.request.get('/api/v1/bff/antiforgery')).json();
  const auditBefore = auditCount();
  const sessionsBefore = sessionCount();
  await page.goto('/');
  const navigations: string[] = [];
  page.on('request', (request) => request.isNavigationRequest() && navigations.push(request.url()));
  await page.evaluate((token) => {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/api/v1/bff/logout';
    form.innerHTML = `<input type="hidden" name="__RequestVerificationToken" value="${token}">`;
    document.body.appendChild(form);
    form.submit();
  }, requestToken);

  await expect.poll(() => {
    const endSessionIndex = navigations.findIndex((url) => url.includes('/oidc/v1/end_session'));
    return endSessionIndex >= 0 && navigations.slice(endSessionIndex + 1).includes(`${origin}/`);
  }).toBe(true);
  await page.waitForLoadState('load');
  expect(page.url()).toBe(`${origin}/`);

  const endSession = new URL(navigations.find((url) => url.includes('/oidc/v1/end_session'))!);
  expect(endSession.searchParams.get('client_id')).toBeTruthy();
  expect(endSession.searchParams.get('id_token_hint')).toMatch(/^eyJ/);
  expect(endSession.searchParams.get('state')).toBeTruthy();
  expect(endSession.searchParams.get('post_logout_redirect_uri')).toBe(`${origin}/api/signout-callback-oidc`);
  const callbackIndex = navigations.findIndex((url) => url.startsWith(`${origin}/api/signout-callback-oidc`));
  expect(callbackIndex).toBeGreaterThan(navigations.findIndex((url) => url.includes('/oidc/v1/end_session')));
  expect(new URL(navigations[callbackIndex]).searchParams.get('state')).toBe(endSession.searchParams.get('state'));

  expect(await (await page.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'anonymous' });
  expect(sessionCount()).toBe(sessionsBefore - 1);
  expect(await page.context().cookies(origin)).toEqual([]);

  expect(auditCount()).toBe(auditBefore + 1);
  const [row] = auditRowsAfter(auditBefore);
  const [action, result, actorType, actorId, customerId, traceId] = row.split('|');
  expect([action, result, actorType, actorId]).toEqual(['SignOut', 'Succeeded', 'Customer', customerA.userId]);
  expect(customerId).toBe(queryDatabase(`select "CustomerId" from "CustomerLinks" where "LoginSub" = '${customerA.userId}'`));
  expect(traceId).toMatch(/^[0-9a-f]{32}$/);

  const clean = await browser.newContext();
  const replay = await clean.request.get(`${origin}/api/v1/transactions?limit=1`, {
    headers: { Cookie: `${sessionCookieName}=${oldSessionCookie}` }, maxRedirects: 0,
  });
  expect(replay.status()).toBe(401);
  expect(expectProblemShape(await replay.json(), 'unauthorized', 401)).toEqual([]);
  await clean.close();
});

test('an unlinked login signs in with a null customer ID in its audit row', async ({ browser }) => {
  const auditBefore = auditCount();
  const context = await browser.newContext();
  const unlinkedPage = await context.newPage();
  await signIn(unlinkedPage, customerC);
  const rows = auditRowsAfter(auditBefore).map((row) => row.split('|').slice(0, 5).join('|'));
  expect(rows).toEqual([`SignIn|Succeeded|Customer|${customerC.userId}|null`]);
  expect(await (await unlinkedPage.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'unlinked' });
  await context.close();
});

test('the API logs hold no ID token, email or password from these sign-ins and sign-outs', async () => {
  const logs = apiLogsSinceStart();
  expect(logs.length).toBeGreaterThan(0);
  expect(logs).not.toMatch(/eyJ[A-Za-z0-9_-]{10,}/);
  expect(logs).not.toContain('id_token_hint');
  for (const secret of [customerA.email, customerC.email, customerA.password, customerC.password]) {
    expect(logs).not.toContain(secret);
  }
});
