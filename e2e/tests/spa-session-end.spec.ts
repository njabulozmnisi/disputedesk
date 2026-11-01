import { expect, request as playwrightRequest, test, type BrowserContext, type Page } from '@playwright/test';
import { origin, sessionCookieName, signInContext } from './support';

// Story 7.14: customer data leaves the browser's memory when a session ends, in every way it can end, through the real BFF.
// "Memory" is inspected through what the page can show: the whole DOM (visible or not) holds none of the customer's
// transaction or dispute identifiers (the start page's sample dispute holds a fixed amount, so amounts are not a signal), and Back shows none. The server's session is ended from outside the page
// (a logout POST with the page's cookies) where the test needs a 401.

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
  const token = (await (await outside.get('/api/v1/bff/antiforgery')).json()).requestToken;
  const logout = await outside.post('/api/v1/bff/logout', { form: { __RequestVerificationToken: token }, maxRedirects: 0 });
  expect(logout.status()).toBe(302);
  await outside.dispose();
}

test.describe.configure({ mode: 'serial' });

test('sign-out in a second tab clears the first tab: the start page, no customer data in the DOM, none after Back', async ({ browser }) => {
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
  // Criterion 4: the receiving tab initiates no request at all (QA finding F2, 7.14-qa.md, fixed in QA fix round 1).
  const requestsAfterSignOut: string[] = [];
  first.on('request', (request) => requestsAfterSignOut.push(`${request.method()} ${new URL(request.url()).pathname}`));
  const startPage = second.waitForResponse((response) => response.url() === `${origin}/` && response.request().isNavigationRequest());
  await second.getByRole('button', { name: 'Sign out' }).click();
  await startPage;

  // The first tab was told by the channel: the start page, with no customer data and no request of its own.
  expect(requestsAfterSignOut, 'requests the receiving tab started').toEqual([]);
  await expect(first).toHaveURL(`${origin}/`);
  await expect(first.getByRole('heading', { name: /Something wrong with a card transaction/ })).toBeVisible();
  await expectNoCustomerData(first, data, 'first tab after the second tab signed out');

  // Back goes to the list the person was on: the route is protected and the session is anonymous, so nothing from memory shows.
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

test('a second tab also ends on the first tab\'s 401 (expired is posted), with no customer data left', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const data = await customerStrings(context);
  const first = await context.newPage();
  await first.goto('/transactions');
  await expect(first.locator('li')).toHaveCount(20);
  const second = await context.newPage();
  await second.goto('/transactions');
  await expect(second.locator('li')).toHaveCount(20);
  await endSessionOutsideThePage(context);
  const secondTabRequests: string[] = [];
  second.on('request', (request) => secondTabRequests.push(`${request.method()} ${new URL(request.url()).pathname}`));
  await first.getByRole('button', { name: 'Load more' }).click();
  await expect(first.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expect(second.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  await expectNoCustomerData(second, data, 'second tab after the first tab\'s 401');
  expect(secondTabRequests.filter((request) => /\/api\/v1\/(transactions|disputes)/.test(request)), 'customer requests the receiving tab started').toEqual([]);
  await context.close();
});

test('a page restored from the back/forward cache reloads; a pageshow that is not a restore does nothing', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const page = await context.newPage();
  await page.goto('/transactions');
  await expect(page.locator('li')).toHaveCount(20);
  const marker = () => page.evaluate(() => (window as unknown as { qaMarker?: string }).qaMarker ?? null);
  await page.evaluate(() => {
    (window as unknown as { qaMarker?: string }).qaMarker = 'before';
    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: false }));
  });
  await page.waitForTimeout(1000);
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
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await page.waitForTimeout(500);
    expect(await page.evaluate(() => document.activeElement === document.body), `${path}: focus left alone on load`).toBe(true);
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Skip to main content' }), path).toBeFocused();
    await page.close();
  }
  await context.close();
});
