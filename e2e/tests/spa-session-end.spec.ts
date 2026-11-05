import { expect, request as playwrightRequest, test, type BrowserContext, type Page } from '@playwright/test';
import { origin, sessionCookieName, signInContext } from './support';

// Every session ends in the browser by a document navigation
// (`location.replace`), so the whole JavaScript heap with the customer's data is dropped. It is inspected through what the
// page can show: the whole DOM (visible or not) holds none of the customer's transaction or dispute identifiers (the start
// page's sample dispute holds a fixed amount, so amounts are not a signal), and Back shows none. Every way out is checked: a
// 401, the idle limit, a sign-out and another tab's end (learnt when that tab is brought to the front: no message between
// tabs). The server's session is ended from outside the page (a logout POST with the page's cookies) where the test needs a 401.

type CustomerData = { strings: string[] };

/** What the customer's own API answers hold that must not remain in the page once the session is over. */
async function customerStrings(context: BrowserContext): Promise<CustomerData> {
  const transactions = await (await context.request.get('/api/v1/transactions?limit=20')).json();
  const disputes = await (await context.request.get('/api/v1/disputes')).json();
  const strings = [
    ...transactions.items.map((row: { transactionId: string }) => row.transactionId),
    ...disputes.items.map((row: { disputeId: string }) => row.disputeId),
  ];
  expect(strings.length).toBeGreaterThan(10);
  return { strings };
}

async function expectNoCustomerData(page: Page, data: CustomerData, label: string) {
  const html = await page.evaluate(() => document.documentElement.outerHTML);
  const found = data.strings.filter((value) => html.includes(value));
  expect(found, `${label}: customer identifiers still in the DOM`).toEqual([]);
}

async function endSessionOutsideThePage(context: BrowserContext) {
  const outside = await playwrightRequest.newContext({ baseURL: origin, storageState: await context.storageState() });
  const token = (await (await outside.get('/api/v1/antiforgery')).json()).requestToken;
  const logout = await outside.post('/api/v1/authentication/logout', { form: { __RequestVerificationToken: token }, maxRedirects: 0 });
  expect(logout.status()).toBe(302);
  await outside.dispose();
}

/** Requests a page starts from now on, as `METHOD /path`. */
function recordRequests(page: Page): string[] {
  const seen: string[] = [];
  page.on('request', (request) => seen.push(`${request.method()} ${new URL(request.url()).pathname}`));
  return seen;
}

/** The tab gains focus and becomes visible, as a person switching to it does (TanStack Query refetches the session query on it). */
async function bringToFront(page: Page) {
  await page.bringToFront();
  await page.evaluate(() => {
    window.dispatchEvent(new Event('focus'));
    document.dispatchEvent(new Event('visibilitychange'));
  });
}

test.describe.configure({ mode: 'serial' });

test('sign-out in a second tab: the second tab ends on the start page, the first leaves to /signed-out when brought to the front, with no customer data in either DOM, none after Back', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const data = await customerStrings(context);
  const first = await context.newPage();
  await first.goto('/transactions');
  await expect(first.locator('li')).toHaveCount(20);
  await first.getByRole('link', { name: /^Dispute status: / }).first().click();
  await expect(first).toHaveURL(/\/disputes\/[\w-]+$/);
  await first.goBack();
  await expect(first.locator('li')).toHaveCount(20);

  const second = await context.newPage();
  await second.goto('/disputes');
  await expect(second.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  const firstTabRequests = recordRequests(first);
  const startPage = second.waitForResponse((response) => response.url() === `${origin}/` && response.request().isNavigationRequest());
  await second.getByRole('button', { name: 'Sign out' }).click();
  await startPage;

  // The signing-out tab: a document navigation to the start page, with no customer data.
  await expect(second).toHaveURL(`${origin}/`);
  await expect(second.getByRole('heading', { name: /Something wrong with a card transaction/ })).toBeVisible();
  await expectNoCustomerData(second, data, 'signing-out tab');

  // Criterion 4: no message reaches the first tab. It stays as it was, starts no request, until it is brought to the front.
  expect(firstTabRequests, 'requests the first tab started before it gained focus').toEqual([]);
  await expect(first).toHaveURL(`${origin}/transactions`);
  await expect(first.locator('li')).toHaveCount(20);

  await bringToFront(first);
  await expect(first).toHaveURL(`${origin}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
  await expect(first.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expectNoCustomerData(first, data, 'first tab after the second tab signed out');
  // The only request the first tab made is the session query (its focus refetch); no customer data was fetched again.
  expect(firstTabRequests.filter((request) => /\/api\/v1\/(transactions|disputes)/.test(request)), 'customer requests the first tab started').toEqual([]);
  expect(firstTabRequests.filter((request) => request === 'GET /api/v1/authentication/session')).toHaveLength(1);

  // Back goes to the document before the session-ended one (replace kept the ended page's predecessor out of the history): nothing from memory shows.
  await first.goBack();
  await first.waitForLoadState('load');
  await expectNoCustomerData(first, data, 'first tab after Back');
  await expect(first.getByRole('listitem').filter({ hasText: /R\d/ })).toHaveCount(0);
  await context.close();
});

test('a 401 while signed in shows the session-ended screen with no customer data in the DOM, also after Back', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const data = await customerStrings(context);
  const page = await context.newPage();
  await page.goto('/transactions');
  await expect(page.locator('li')).toHaveCount(20);
  await endSessionOutsideThePage(context);
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page).toHaveURL(`${origin}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
  await expect(page.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expectNoCustomerData(page, data, 'session-ended screen');

  await page.goBack();
  await page.waitForLoadState('load');
  await expectNoCustomerData(page, data, 'after Back from the session-ended screen');
  await context.close();
});

test('a second tab ends on the first tab\'s 401 when it is brought to the front: /signed-out, no customer data left, also after Back', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const data = await customerStrings(context);
  const first = await context.newPage();
  await first.goto('/transactions');
  await expect(first.locator('li')).toHaveCount(20);
  const second = await context.newPage();
  await second.goto('/transactions');
  await expect(second.locator('li')).toHaveCount(20);
  await endSessionOutsideThePage(context);
  const secondTabRequests = recordRequests(second);
  await first.getByRole('button', { name: 'Load more' }).click();
  await expect(first).toHaveURL(`${origin}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
  await expect(first.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expectNoCustomerData(first, data, 'first tab after its own 401');

  // No message reaches the second tab: it learns only when brought to the front (or on its own 401 or deadline).
  expect(secondTabRequests, 'requests the second tab started before it gained focus').toEqual([]);
  await expect(second.locator('li')).toHaveCount(20);

  await bringToFront(second);
  await expect(second).toHaveURL(`${origin}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
  await expect(second.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expectNoCustomerData(second, data, 'second tab after it gained focus');
  expect(secondTabRequests.filter((request) => /\/api\/v1\/(transactions|disputes)/.test(request)), 'customer requests the receiving tab started').toEqual([]);

  await second.goBack();
  await second.waitForLoadState('load');
  await expectNoCustomerData(second, data, 'second tab after Back');
  await context.close();
});

test('the idle limit ends the session with a document navigation to /signed-out: no customer data in the DOM, also after Back', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const data = await customerStrings(context);
  const page = await context.newPage();
  await page.clock.install();
  await page.goto('/transactions');
  await expect(page.locator('li')).toHaveCount(20);
  await page.clock.runFor('13:01');
  await expect(page.getByRole('alertdialog')).toBeVisible();
  await page.clock.runFor('02:00');
  await expect(page).toHaveURL(`${origin}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
  await expect(page.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expectNoCustomerData(page, data, 'session-ended screen after the idle limit');

  await page.goBack();
  await page.waitForLoadState('load');
  await expectNoCustomerData(page, data, 'after Back from the idle-limit session-ended screen');
  await context.close();
});

test('a page restored from the back/forward cache reloads; a pageshow that is not a restore does nothing', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const page = await context.newPage();
  await page.goto('/transactions');
  await expect(page.locator('li')).toHaveCount(20);
  const marker = () => page.evaluate(() => (window as unknown as { qaMarker?: string }).qaMarker ?? null);
  const noReload = expect(page.waitForEvent('load', { timeout: 1000 })).rejects.toThrow('Timeout');
  await page.evaluate(() => {
    (window as unknown as { qaMarker?: string }).qaMarker = 'before';
    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: false }));
  });
  await noReload;
  expect(await marker()).toBe('before');

  const reloaded = page.waitForEvent('load');
  await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true })));
  await reloaded;
  await expect(page.locator('li')).toHaveCount(20);
  expect(await marker()).toBeNull();
  await context.close();
});

test('direct loads of an unknown path and of /signed-out?reason=expired while signed in: the first Tab reaches the skip link', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  expect((await context.cookies()).some((cookie) => cookie.name === sessionCookieName)).toBe(true);
  for (const path of ['/no/such/page', '/signed-out?reason=expired']) {
    const page = await context.newPage();
    await page.setViewportSize({ width: 1280, height: 800 });
    const requests = recordRequests(page);
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    if (path.startsWith('/signed-out')) {
      // Criterion 2: the session-ended screen makes no API request (a request with the live cookie would renew the ended session).
      await page.waitForLoadState('networkidle');
      expect(requests.filter((request) => request.split(' ')[1].startsWith('/api/')), `${path}: API requests`).toEqual([]);
    }
    await expect(page.waitForFunction(() => document.activeElement !== document.body, undefined, { timeout: 500 }), `${path}: focus left alone on load`).rejects.toThrow('Timeout');
    expect(await page.evaluate(() => document.activeElement === document.body), `${path}: focus left alone on load`).toBe(true);
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Skip to main content' }), path).toBeFocused();
    await page.close();
  }
  await context.close();
});
