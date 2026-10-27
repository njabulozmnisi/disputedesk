import { expect, test, type Page } from '@playwright/test';
import {
  antiforgeryCookieName, expectProblemShape, fixtureLogin, queryDatabase, sessionCookieName,
} from './support';

// Story 2.2: BFF sign-in and sign-out through the hosted Zitadel login, one browser, one login (customer A).
test.describe.configure({ mode: 'serial' });

let page: Page;
let oldSessionCookie = '';
let rowsBefore = 0;
const sessionRows = () => Number(queryDatabase('select count(*) from "Sessions"'));
const customerA = fixtureLogin('customer-a');

async function signInThroughHostedLogin(target: Page, loginUrl: string) {
  await target.goto(loginUrl);
  await target.getByTestId('username-text-input').fill(customerA.username);
  await target.getByTestId('username-text-input').press('Enter');
  await target.getByTestId('password-text-input').fill(customerA.password);
  await target.getByTestId('password-text-input').press('Enter');
  await target.waitForURL((url) => url.origin === 'http://localhost:8080');
}

test.beforeAll(async ({ browser }) => {
  page = await (await browser.newContext()).newPage();
  rowsBefore = sessionRows();
});

test.afterAll(async () => {
  await page.context().close();
});

test('anonymous caller: session is anonymous and antiforgery is a 401 problem', async ({ request }) => {
  const session = await request.get('/api/v1/bff/session');
  expect(await session.json()).toEqual({ state: 'anonymous' });

  const antiforgery = await request.get('/api/v1/bff/antiforgery');
  expect(antiforgery.status()).toBe(401);
  expect(antiforgery.headers()['content-type']).toContain('application/problem+json');
  expect(expectProblemShape(await antiforgery.json(), 'unauthorized', 401)).toEqual([]);
});

test('sign-in lands on / with an unlinked session and one hashed row', async () => {
  await signInThroughHostedLogin(page, '/api/v1/bff/login');
  expect(new URL(page.url()).pathname).toBe('/');

  const session = await page.request.get('/api/v1/bff/session');
  expect(await session.json()).toEqual({ state: 'unlinked' });

  expect(sessionRows()).toBe(rowsBefore + 1);
  expect(queryDatabase('select min(length("KeyHash")) from "Sessions"')).toBe('32');
});

test('the browser holds only an HttpOnly SameSite=Strict session cookie and no token', async () => {
  await page.request.get('/api/v1/bff/antiforgery');
  const cookies = (await page.context().cookies('http://localhost:8080'));
  expect(cookies.map((cookie) => cookie.name).sort()).toEqual([antiforgeryCookieName, sessionCookieName]);
  for (const cookie of cookies) {
    expect(cookie.httpOnly).toBe(true);
    expect(cookie.sameSite).toBe('Strict');
    expect(cookie.value).not.toMatch(/^eyJ/);
  }
  oldSessionCookie = cookies.find((cookie) => cookie.name === sessionCookieName)!.value;

  const storage = await page.evaluate(() => ({
    local: Object.keys(localStorage), session: Object.keys(sessionStorage), readable: document.cookie,
  }));
  expect(storage).toEqual({ local: [], session: [], readable: '' });

  const bodies = [
    await (await page.request.get('/api/v1/bff/session')).text(),
    await (await page.request.get('/api/v1/bff/antiforgery')).text(),
  ].join();
  expect(bodies).not.toMatch(/eyJ|access_token|id_token|refresh_token/);
});

test('a missing or forged antiforgery token gets a 400 validation problem and keeps the session', async () => {
  const missing = await page.request.post('/api/v1/bff/logout', { maxRedirects: 0 });
  expect(missing.status()).toBe(400);
  expect(missing.headers()['content-type']).toContain('application/problem+json');
  expect(expectProblemShape(await missing.json(), 'validation', 400)).toEqual([]);

  const forged = await page.request.post('/api/v1/bff/logout', {
    headers: { 'X-XSRF-TOKEN': 'forged' }, maxRedirects: 0,
  });
  expect(forged.status()).toBe(400);
  expect(expectProblemShape(await forged.json(), 'validation', 400)).toEqual([]);

  expect(sessionRows()).toBe(rowsBefore + 1);
});

test('logout with the token deletes the row, clears both cookies and redirects to end_session with an id_token_hint', async ({ browser }) => {
  const { requestToken } = await (await page.request.get('/api/v1/bff/antiforgery')).json();
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

  // No Zitadel logout or account-selection page in between: the browser goes straight back to /.
  // The page is already at /, so wait for the navigation chain itself: end_session, then / again.
  await expect.poll(() => {
    const endSessionIndex = navigations.findIndex((url) => url.includes('/oidc/v1/end_session'));
    return endSessionIndex >= 0 && navigations.slice(endSessionIndex + 1).includes('http://localhost:8080/');
  }).toBe(true);
  await page.waitForLoadState('load');
  expect(page.url()).toBe('http://localhost:8080/');
  expect(navigations.filter((url) => url.includes('/ui/v2/login/logout'))).toEqual([]);

  const endSession = new URL(navigations.find((url) => url.includes('/oidc/v1/end_session'))!);
  expect(endSession.searchParams.get('client_id')).toBeTruthy();
  expect(endSession.searchParams.get('post_logout_redirect_uri')).toBe('http://localhost:8080/');
  expect(endSession.searchParams.get('id_token_hint')).toBeTruthy();

  expect(await (await page.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'anonymous' });

  expect(sessionRows()).toBe(rowsBefore);
  expect(await page.context().cookies('http://localhost:8080')).toEqual([]);

  // Replay of the old cookie in a clean context.
  const clean = await browser.newContext();
  const replay = await clean.request.get('http://localhost:8080/api/v1/bff/antiforgery', {
    headers: { Cookie: `${sessionCookieName}=${oldSessionCookie}` },
  });
  expect(replay.status()).toBe(401);
  expect(expectProblemShape(await replay.json(), 'unauthorized', 401)).toEqual([]);
  await clean.close();
});

test('the Zitadel session really ended: a fresh login needs the password', async () => {
  const silentSignIn: string[] = [];
  const watch = (request: { isNavigationRequest(): boolean; url(): string }) =>
    request.isNavigationRequest() && request.url().includes('/api/signin-oidc') && silentSignIn.push(request.url());
  page.on('request', watch);

  await page.goto('/api/v1/bff/login');
  // Zitadel remembers the username, so it may show an account picker; choosing the account must still ask for the password.
  const account = page.getByRole('button', { name: new RegExp(customerA.username) });
  const passwordField = page.getByTestId('password-text-input');
  await expect(account.or(passwordField).or(page.getByTestId('username-text-input'))).toBeVisible();
  if (await account.isVisible()) {
    await account.click();
  }
  if (await page.getByTestId('username-text-input').isVisible()) {
    await page.getByTestId('username-text-input').fill(customerA.username);
    await page.getByTestId('username-text-input').press('Enter');
  }
  await expect(passwordField).toBeVisible();
  expect(new URL(page.url()).pathname).toBe('/ui/v2/login/password');
  expect(silentSignIn).toEqual([]);
  page.off('request', watch);
});

test('with the password the sign-in completes, and a non-local returnUrl lands on /', async () => {
  await page.goto('/api/v1/bff/login?returnUrl=https://evil.example.com/steal');
  const account = page.getByRole('button', { name: new RegExp(customerA.username) });
  const passwordField = page.getByTestId('password-text-input');
  await expect(account.or(passwordField).or(page.getByTestId('username-text-input'))).toBeVisible();
  if (await account.isVisible()) {
    await account.click();
  }
  if (await page.getByTestId('username-text-input').isVisible()) {
    await page.getByTestId('username-text-input').fill(customerA.username);
    await page.getByTestId('username-text-input').press('Enter');
  }
  await passwordField.fill(customerA.password);
  await passwordField.press('Enter');
  await page.waitForURL((url) => url.origin === 'http://localhost:8080');
  expect(page.url()).toBe('http://localhost:8080/');
  expect(sessionRows()).toBe(rowsBefore + 1);
});

test('error bodies share one shape', async ({ request }) => {
  const notFound = await request.get('/api/v1/does-not-exist');
  expect(notFound.status()).toBe(404);
  expect(expectProblemShape(await notFound.json(), 'not-found', 404)).toEqual([]);

  const tooLong = await request.get(`/api/v1/bff/login?returnUrl=${'a'.repeat(2100)}`, { maxRedirects: 0 });
  expect(tooLong.status()).toBe(400);
  const body = await tooLong.json();
  expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
  expect(Object.keys(body.errors)).toEqual(['returnUrl']);
  expect(JSON.stringify(body)).not.toContain('aaaa');
});
