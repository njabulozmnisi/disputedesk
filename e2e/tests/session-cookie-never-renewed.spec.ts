import { expect, test, type Page, type Response } from '@playwright/test';
import { fixtureLogin, queryDatabase, sessionCookieName } from './support';

// Session fix (AD-7, Expiry): the session cookie is written at sign-in and cleared at sign-out, never renewed. The ticket
// store slides the row's idle limit on each request; the absolute limit never moves. One browser, customer A.
test.describe.configure({ mode: 'serial' });

const customerA = fixtureLogin('customer-a');
let page: Page;
let sessionId = '';

const sessionSetCookies: string[] = [];
async function recordSessionSetCookie(response: Response) {
  const setCookies = (await response.headersArray()).filter((header) => header.name.toLowerCase() === 'set-cookie');
  for (const header of setCookies) {
    if (header.value.startsWith(`${sessionCookieName}=`)) sessionSetCookies.push(`${response.status()} ${response.url()}`);
  }
}

const sessionRow = () => {
  const [expires, absolute] = queryDatabase(
    `select extract(epoch from "ExpiresAtUtc"), extract(epoch from "AbsoluteExpiresAtUtc") from "Sessions" where "Id" = '${sessionId}'`,
  ).split('|').map(Number);
  return { expires, absolute };
};
const pause = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

test.beforeAll(async ({ browser }) => {
  page = await (await browser.newContext()).newPage();
});
test.afterAll(async () => {
  await page.context().close();
});

async function signIn() {
  const knownIds = queryDatabase('select "Id" from "Sessions"').split('\n').filter(Boolean);
  await page.goto('/api/v1/bff/login');
  // After a logout Zitadel may show an account picker or the username step; the password is always asked.
  const account = page.getByRole('button', { name: new RegExp(customerA.username) });
  const username = page.getByTestId('username-text-input');
  const password = page.getByTestId('password-text-input');
  await expect(account.or(username).or(password)).toBeVisible();
  if (await account.isVisible()) await account.click();
  if (await username.isVisible()) {
    await username.fill(customerA.username);
    await username.press('Enter');
  }
  await password.fill(customerA.password);
  await password.press('Enter');
  await page.waitForURL((url) => url.origin === 'http://localhost:8080');
  const created = queryDatabase('select "Id" from "Sessions"').split('\n').filter((id) => id && !knownIds.includes(id));
  expect(created).toHaveLength(1);
  return created[0];
}

test('sign-in sets the session cookie once; the row has a 15 minute idle and a 12 hour absolute limit', async () => {
  const signInSetCookies: Response[] = [];
  page.on('response', async (response) => {
    const names = (await response.headersArray()).filter((h) => h.name.toLowerCase() === 'set-cookie' && h.value.startsWith(`${sessionCookieName}=`));
    if (names.length > 0) signInSetCookies.push(response);
  });
  sessionId = await signIn();
  page.removeAllListeners('response');

  expect(signInSetCookies.map((response) => new URL(response.url()).pathname)).toEqual(['/api/signin-oidc']);
  const cookie = (await page.context().cookies('http://localhost:8080')).find((c) => c.name === sessionCookieName)!;
  expect(cookie.expires).toBe(-1); // a browser-session cookie: no Expires, no renewal to extend

  const { expires, absolute } = sessionRow();
  const now = Date.now() / 1000;
  expect(expires - now).toBeGreaterThan(14 * 60);
  expect(expires - now).toBeLessThanOrEqual(15 * 60 + 5);
  expect(absolute - now).toBeGreaterThan(12 * 3600 - 60);
  expect(absolute - now).toBeLessThanOrEqual(12 * 3600 + 5);
});

test('no later response sets the session cookie, while ExpiresAtUtc moves forward on each request and AbsoluteExpiresAtUtc never does', async () => {
  page.on('response', recordSessionSetCookie);
  const first = sessionRow();
  let previous = first.expires;

  const requests = [
    () => page.request.get('/api/v1/bff/session'),
    () => page.request.get('/api/v1/bff/antiforgery'),
    () => page.request.get('/api/v1/transactions'),
    () => page.request.get('/api/v1/bff/session'),
    () => page.request.get('/api/v1/bff/session'),
  ];
  for (const send of requests) {
    await pause(1100);
    const response = await send();
    expect(response.status()).toBe(200);
    const setCookie = (await response.headersArray()).filter((h) => h.name.toLowerCase() === 'set-cookie');
    expect(setCookie.filter((h) => h.value.startsWith(`${sessionCookieName}=`))).toEqual([]);
    const row = sessionRow();
    expect(row.expires).toBeGreaterThan(previous);
    expect(row.absolute).toBe(first.absolute);
    previous = row.expires;
  }

  // Browser navigations (SPA document, its API calls) as well.
  await page.goto('/');
  await page.waitForLoadState('networkidle');
  await pause(500);
  page.off('response', recordSessionSetCookie);
  expect(sessionSetCookies).toEqual([]);
  expect(sessionRow().absolute).toBe(first.absolute);
  expect(sessionRow().expires).toBeGreaterThan(first.expires);
});

test('an anonymous or dead-cookie request does not slide anything: a forged key creates no row and gets 401', async ({ playwright }) => {
  const rowsBefore = queryDatabase('select count(*) from "Sessions"');
  const stranger = await playwright.request.newContext({ baseURL: 'http://localhost:8080', extraHTTPHeaders: { Cookie: `${sessionCookieName}=forged` } });
  const response = await stranger.get('/api/v1/bff/antiforgery');
  expect(response.status()).toBe(401);
  expect((await response.headersArray()).filter((h) => h.name.toLowerCase() === 'set-cookie').filter((h) => h.value.startsWith(`${sessionCookieName}=`))).toEqual([]);
  expect(queryDatabase('select count(*) from "Sessions"')).toBe(rowsBefore);
  await stranger.dispose();
});

test('logout with /session requests in flight leaves no session cookie in the browser, over repeated rounds', async () => {
  test.setTimeout(180_000);
  const rounds = 5;
  for (let round = 1; round <= rounds; round++) {
    if (round > 1) sessionId = await signIn();
    expect((await page.context().cookies('http://localhost:8080')).map((c) => c.name)).toContain(sessionCookieName);

    // A second page in the same browser context hammers /session (each call carries the cookie) while this page logs out.
    const hammer = await page.context().newPage();
    await hammer.goto('/');
    // Paced under the gateway's 10 requests a second (infra/nginx), so a 429 never stands in for a real in-flight response.
    await hammer.evaluate(() => {
      const state = window as unknown as { stop: boolean; statuses: number[]; inFlight: Promise<void> };
      state.stop = false;
      state.statuses = [];
      state.inFlight = (async () => {
        while (!state.stop) {
          const responses = await Promise.all(Array.from({ length: 2 }, () => fetch('/api/v1/bff/session', { credentials: 'include' })));
          state.statuses.push(...responses.map((response) => response.status));
          await new Promise((resolve) => setTimeout(resolve, 200));
        }
      })();
    });

    const { requestToken } = await (await page.request.get('/api/v1/bff/antiforgery')).json();
    await page.goto('/');
    await page.evaluate((token) => {
      const form = document.createElement('form');
      form.method = 'POST';
      form.action = '/api/v1/bff/logout';
      form.innerHTML = `<input type="hidden" name="__RequestVerificationToken" value="${token}">`;
      document.body.appendChild(form);
      form.submit();
    }, requestToken);
    await page.waitForURL((url) => url.href === 'http://localhost:8080/', { timeout: 20_000 });
    await page.waitForLoadState('load');

    // Let the hammering continue past the logout response, then stop it and wait for every request to finish.
    await pause(750);
    const statuses = await hammer.evaluate(async () => {
      const state = window as unknown as { stop: boolean; statuses: number[]; inFlight: Promise<void> };
      state.stop = true;
      await state.inFlight;
      return state.statuses;
    });
    expect(statuses.length).toBeGreaterThan(2);
    expect(statuses.filter((status) => status !== 200)).toEqual([]);
    await hammer.close();

    expect(await page.context().cookies('http://localhost:8080')).toEqual([]);
    expect(queryDatabase(`select count(*) from "Sessions" where "Id" = '${sessionId}'`)).toBe('0');
  }
});
