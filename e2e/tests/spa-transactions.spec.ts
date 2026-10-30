import AxeBuilder from '@axe-core/playwright';
import { expect, request as playwrightRequest, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { fixtureLogin, gatewayContentSecurityPolicy, queryDatabase, sessionCookieName } from './support';

// Story 2.4: the SPA shell, session routing and the transactions screen, through the real BFF and Zitadel.
// Since story 2.5 the gateway serves the SPA, so SPA_ORIGIN defaults to the gateway and the browser sees the real
// security headers. Set SPA_ORIGIN to point somewhere else (for example `vite preview` on :4173, which sends no CSP).
// The browser clock for the inactivity warning is Playwright's, so no test waits 13 minutes.

const spa = process.env.SPA_ORIGIN ?? 'http://localhost:8080';
const gateway = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const coreBankingContainer = 'disputedesk-core-banking-1';
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

type ApiRow = {
  transactionId: string;
  transactionDate: string;
  amount: { amount: string; currency: string };
  direction: 'debit' | 'credit';
  disputable: boolean;
  notDisputableReason: string | null;
  disputeId?: string | null;
  customerStatus?: string | null;
};
type ApiPage = { items: ApiRow[]; nextCursor: string | null };

/** The one console error accepted by decision (story 2.5 criterion 7): Radix's scroll-lock <style> under style-src 'self'. */
const knownRadixStyleViolation = /^Applying inline style violates the following Content Security Policy directive 'style-src 'self''\./;

const weekdays = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** South African time is UTC+2 all year (no daylight saving), so the expected day is plain arithmetic. */
function expectedDay(isoDateTime: string): string {
  const johannesburg = new Date(new Date(isoDateTime).getTime() + 2 * 60 * 60 * 1000);
  return `${weekdays[johannesburg.getUTCDay()]} ${johannesburg.getUTCDate()} ${months[johannesburg.getUTCMonth()]}`;
}

function expectedAmount(row: ApiRow): string {
  const [whole, cents] = row.amount.amount.split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  return `${row.direction === 'credit' ? '+' : ''}R${grouped}.${cents}`;
}

async function signIn(browser: Browser, loginKey: string, timezoneId?: string): Promise<{ context: BrowserContext; page: Page }> {
  const login = fixtureLogin(loginKey);
  const context = await browser.newContext({ timezoneId });
  const page = await context.newPage();
  await page.goto(`${spa}/api/v1/bff/login`);
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === gateway, { timeout: 60_000 });
  return { context, page };
}

/**
 * vite preview sends no CSP; add the gateway's to the SPA documents so the console shows what nginx would block.
 * On the gateway 'self' is the gateway origin, which is where Zitadel sends the browser after end-session; here the
 * SPA is on another port, so that origin is added to form-action to stand in for it.
 */
async function withGatewayCsp(page: Page) {
  if (spa === gateway) return; // the gateway sends its own CSP; do not replace it with a copy
  await page.route(`${spa}/**`, async (route) => {
    const request = route.request();
    if (request.resourceType() !== 'document' || request.method() !== 'GET' || new URL(request.url()).pathname.startsWith('/api/')) return route.fallback();
    const response = await route.fetch();
    await route.fulfill({ response, headers: { ...response.headers(), 'content-security-policy': gatewayContentSecurityPolicy.replace("form-action 'self'", `form-action 'self' ${gateway}`) } });
  });
}

/** Ends the server session from outside the page: the same cookies, a fresh request token, the logout POST (not followed). */
async function endSessionOutsideThePage(context: BrowserContext) {
  const outside = await playwrightRequest.newContext({ baseURL: gateway, storageState: await context.storageState() });
  const token = (await (await outside.get('/api/v1/bff/antiforgery')).json()).requestToken;
  const logout = await outside.post('/api/v1/bff/logout', { form: { __RequestVerificationToken: token }, maxRedirects: 0 });
  expect(logout.status()).toBe(302);
  await outside.dispose();
}

async function expectNoAxeViolations(page: Page) {
  const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  expect(result.violations.map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join('; ')}`)).toEqual([]);
}

async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(0);
}

async function pageText(page: Page): Promise<string> {
  return (await page.locator('body').innerText()).replace(/\s+/g, ' ');
}

test.describe.configure({ mode: 'serial' });

test.describe('anonymous', () => {
  test('routes: start page at /, protected and unbuilt paths, public /signed-out', async ({ page }) => {
    const sessionCalls: number[] = [];
    page.on('response', (response) => {
      if (new URL(response.url()).pathname === '/api/v1/bff/session') sessionCalls.push(response.status());
    });

    await page.goto(`${spa}/`);
    await expect(page.getByRole('heading', { name: /Something wrong with a card transaction/ })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Sign in' }).first()).toHaveAttribute('href', '/api/v1/bff/login');

    await page.goto(`${spa}/transactions`);
    await expect(page).toHaveURL(`${spa}/`);

    // /link is an unlinked-login screen since story 6.4: an anonymous visit goes to the start page, as for /transactions.
    await page.goto(`${spa}/link`);
    await expect(page).toHaveURL(`${spa}/`);
    await expect(page.getByRole('heading', { name: "We can't find that dispute" })).toHaveCount(0);

    for (const path of ['/nope', '/signed-out', '/signed-out?reason=other']) {
      await page.goto(`${spa}${path}`);
      await expect(page.getByRole('heading', { name: "We can't find that dispute" }), path).toBeVisible();
      expect(new URL(page.url()).pathname, path).toBe(path.split('?')[0]);
    }

    // /disputes is a linked-customer screen since story 3.7: anonymous goes to the start page, as for /transactions.
    await page.goto(`${spa}/disputes`);
    await expect(page).toHaveURL(`${spa}/`);

    await page.goto(`${spa}/signed-out?reason=expired`);
    await expect(page.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
    await expect(page.getByText("Anything you hadn't submitted wasn't saved.")).toBeVisible();
    expect(sessionCalls.every((status) => status === 200)).toBe(true);
  });

  test('returnUrl on /signed-out: a local path is kept, every crafted value is dropped', async ({ page }) => {
    const hrefFor = async (returnUrl: string) => {
      await page.goto(`${spa}/signed-out?reason=expired&returnUrl=${returnUrl}`);
      return page.getByRole('link', { name: 'Sign in again' }).getAttribute('href');
    };
    expect(await hrefFor('/transactions')).toBe('/api/v1/bff/login?returnUrl=%2Ftransactions');
    for (const crafted of ['/%5C', '/%09/evil.example', '//evil.example', '/%0a/evil.example', 'https://evil.example', '/\\', '/%5C%5B']) {
      expect(await hrefFor(crafted), crafted).toBe('/api/v1/bff/login');
    }
  });

  test('screens: axe, 320 px without horizontal scroll', async ({ page }) => {
    for (const path of ['/', '/signed-out?reason=expired', '/nope']) {
      await page.setViewportSize({ width: 1280, height: 800 });
      await page.goto(`${spa}${path}`);
      await page.getByRole('heading').first().waitFor();
      await expectNoAxeViolations(page);
      await page.setViewportSize({ width: 320, height: 800 });
      await expectNoHorizontalScroll(page);
    }
  });

  test('a failing session request shows an error with Try again, never the start page', async ({ page }) => {
    let failures = 0;
    await page.route('**/api/v1/bff/session', (route) => {
      failures += 1;
      return failures <= 2 ? route.fulfill({ status: 503, contentType: 'application/problem+json', body: '{}' }) : route.continue();
    });
    await page.goto(`${spa}/`);
    await expect(page.getByText('Something went wrong on our side. Try again.')).toBeVisible();
    await expect(page.getByRole('heading', { name: /Something wrong with a card transaction/ })).toHaveCount(0);
    expect(failures).toBe(2); // one read retry
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByRole('heading', { name: /Something wrong with a card transaction/ })).toBeVisible();
  });
});

test.describe('unlinked customer C', () => {
  test('is sent to /link (the details form since story 6.4), never to /transactions, and no transactions call is made', async ({ browser }) => {
    const { context, page } = await signIn(browser, 'customer-c');
    const transactionCalls: string[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/v1/transactions')) transactionCalls.push(request.url());
    });
    for (const path of ['/', '/transactions']) {
      await page.goto(`${spa}${path}`);
      await expect(page).toHaveURL(`${spa}/link`);
      await expect(page.getByRole('heading', { name: 'Link your login' })).toBeVisible();
      await expect(page.getByRole('heading', { name: "We can't find that dispute" })).toHaveCount(0);
    }
    await page.goto(`${spa}/signed-out?reason=expired`);
    await expect(page.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
    expect(transactionCalls).toEqual([]);
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length, document.cookie])).toEqual([0, 0, '']);
    await context.close();
  });
});

test.describe('linked customer A', () => {
  let context: BrowserContext;
  let page: Page;

  test.beforeAll(async ({ browser }) => {
    ({ context, page } = await signIn(browser, 'customer-a', 'Pacific/Auckland'));
  });
  test.afterAll(async () => context.close());

  test('/ goes to /transactions; list in Johannesburg time, amounts from decimal strings, hints per row', async () => {
    const requests: { url: string; headers: Record<string, string> }[] = [];
    page.on('request', (request) => {
      if (request.url().includes('/api/')) requests.push({ url: request.url(), headers: request.headers() });
    });
    const firstPage = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/transactions');
    await page.goto(`${spa}/`);
    await expect(page).toHaveURL(`${spa}/transactions`);
    const response = await firstPage;
    expect(response.headers()['cache-control']).toBe('no-store');
    const body: ApiPage = await response.json();
    expect(body.items).toHaveLength(20);
    expect(body.items.every((row) => row.transactionId.startsWith('TXN-A-'))).toBe(true);

    const rows = page.locator('li');
    await expect(rows).toHaveCount(20);
    for (const [index, row] of body.items.entries()) {
      const text = await rows.nth(index).innerText();
      expect(text, row.transactionId).toContain(expectedAmount(row).replace(' ', ' '));
    }
    // Each day heading is the Johannesburg day of the rows under it (the browser here is in Auckland, UTC+13).
    const headings = await page.getByRole('heading', { level: 2 }).allInnerTexts();
    expect(headings[0]).toBe(expectedDay(body.items[0].transactionDate));
    expect(new Set(headings.map((heading) => heading)).size).toBe(headings.length);

    // Row rule (stories 3.6, 3.10): a "Dispute this transaction" link exactly when disputable; a "Dispute status: ..."
    // link exactly when disputeId is set, to /disputes/<encoded id>; no hint on alreadyDisputed rows (hint phrases only).
    const hintPhrases = /Pending\. |Money in\. |Transfer between|Not a card payment|Older than 90 days|Not disputable|Contact the bank/;
    for (const [index, row] of body.items.entries()) {
      const li = rows.nth(index);
      await expect(li.getByRole('link', { name: 'Dispute this transaction' }), `${row.transactionId} dispute control`).toHaveCount(row.disputable ? 1 : 0);
      const statusLinks = li.getByRole('link', { name: /^Dispute status: / });
      await expect(statusLinks, `${row.transactionId} status badge`).toHaveCount(row.disputeId ? 1 : 0);
      if (row.disputeId) {
        await expect(statusLinks).toHaveAttribute('href', `/disputes/${encodeURIComponent(row.disputeId)}`);
        await expect(li.getByRole('link')).toHaveCount(1);
        expect(row.disputable, `${row.transactionId} disputed row is not disputable`).toBe(false);
      }
      if (row.notDisputableReason === 'alreadyDisputed') {
        const text = (await li.innerText()).replace(/\s+/g, ' ');
        expect(text, `${row.transactionId} alreadyDisputed shows no hint`).not.toMatch(hintPhrases);
      }
    }
    expect(await pageText(page)).not.toMatch(/escalated/i);
    const allText = await pageText(page);
    expect(allText).toContain('Pending. You can dispute it once it has settled.');
    expect(allText).toContain('Money in. Not disputable.');
    expect(allText).toContain('Transfer between your accounts. Not disputable.');
    expect(allText).toContain('Not a card payment. Contact the bank about this transaction.');

    // The SPA sends cookies only: no Authorization header, no antiforgery header on a GET.
    for (const request of requests) {
      expect(request.headers.authorization).toBeUndefined();
      expect(request.headers['x-xsrf-token']).toBeUndefined();
    }
  });

  test('rows marked alreadyDisputed are exactly the customer’s disputes in the database', async () => {
    const response = await context.request.get(`${spa}/api/v1/transactions?limit=50`);
    const body: ApiPage = await response.json();
    const marked = body.items.filter((row) => row.notDisputableReason === 'alreadyDisputed').map((row) => row.transactionId).sort();
    const inDatabase = queryDatabase('select "TransactionId" from "Disputes" where "CustomerId" = \'CUS-000001\' order by 1').split('\n').filter(Boolean);
    expect(marked.length).toBeGreaterThan(0);
    expect(marked).toEqual(inDatabase);
  });

  test('Load more: next page by cursor, focus on the first new row, polite announcement, button gone at the end', async () => {
    const nextPage = page.waitForResponse((response) => response.url().includes('/api/v1/transactions?cursor='));
    await page.getByRole('button', { name: 'Load more' }).click();
    const response = await nextPage;
    const body: ApiPage = await response.json();
    await expect(page.locator('li')).toHaveCount(20 + body.items.length);
    const focusedText = await page.evaluate(() => (document.activeElement as HTMLElement).innerText.replace(/\s+/g, ' '));
    expect(await page.evaluate(() => [document.activeElement?.tagName, document.activeElement?.getAttribute('tabindex')])).toEqual(['LI', '-1']);
    expect(focusedText).toContain(expectedAmount(body.items[0]));
    await expect(page.locator('[aria-live="polite"]')).toHaveText(`${body.items.length} more transactions loaded`);
    // The fixture's size is not pinned: keep loading until the last page.
    let last = body;
    while (last.nextCursor) {
      const more = page.waitForResponse((answer) => answer.url().includes('/api/v1/transactions?cursor='));
      await page.getByRole('button', { name: 'Load more' }).click();
      last = await (await more).json();
    }
    await expect(page.getByRole('button', { name: 'Load more' })).toHaveCount(0);
    await expect(page.getByText('Older than 90 days. Contact the bank about this transaction.')).toBeVisible();
  });

  test('no token in storage or readable cookies; the session cookie is HttpOnly', async () => {
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length, document.cookie])).toEqual([0, 0, '']);
    const session = (await context.cookies()).find((cookie) => cookie.name === sessionCookieName);
    expect(session?.httpOnly).toBe(true);
    expect(session?.sameSite).toBe('Strict');
  });

  test('transactions screen: axe at 1280 px, 320 px without horizontal scroll, visible focus on every control', async () => {
    await page.goto(`${spa}/transactions`);
    await expect(page.locator('li')).toHaveCount(20);
    await page.setViewportSize({ width: 1280, height: 800 });
    await expectNoAxeViolations(page);
    await page.setViewportSize({ width: 320, height: 800 });
    await expectNoHorizontalScroll(page);
    await page.evaluate(() => window.scrollTo(0, 0));
    // The first screen of a page load leaves focus alone (story 3.7), so the first Tab reaches the skip link, then the logo.
    const visited: string[] = [];
    const problems: string[] = [];
    for (let step = 0; step < 60; step += 1) {
      await page.keyboard.press('Tab');
      const state = await page.evaluate(() => {
        const element = document.activeElement as HTMLElement;
        if (!element || element === document.body) return null;
        const style = getComputedStyle(element);
        const box = element.getBoundingClientRect();
        const hit = document.elementFromPoint(box.left + box.width / 2, Math.min(box.top + box.height / 2, innerHeight - 1));
        return {
          name: (element.innerText || element.getAttribute('aria-label') || element.tagName).replace(/\s+/g, ' ').slice(0, 30),
          outline: style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0,
          shadow: style.boxShadow !== 'none',
          covered: !(hit && (hit === element || element.contains(hit))),
        };
      });
      if (!state) break;
      visited.push(state.name);
      if (!state.outline && !state.shadow) problems.push(`${state.name}: no focus indicator`);
      if (state.covered) problems.push(`${state.name}: covered`);
    }
    expect(problems).toEqual([]);
    // Every control was reached, including the ones at the foot of the page next to the tab bar.
    expect(visited.slice(0, 2)).toEqual(['Skip to main content', 'DisputeDesk']);
    // 'Dispute this transaction' is on the page only while customer A has a disputable row among the first 20; the suite's
    // earlier specs raise disputes on A's 20 disputable transactions, so a full run can leave none.
    const disputeLinkExpected = (await page.getByRole('link', { name: 'Dispute this transaction' }).count()) > 0;
    expect(visited).toEqual(expect.arrayContaining(['Skip to main content', 'DisputeDesk', 'Sign out', 'Load more', '☰ Transactions', '⚑ Disputes']));
    if (disputeLinkExpected) expect(visited).toContain('Dispute this transaction');
  });

  test('sign-out: top-level form POST with a fresh token, Zitadel end-session, back to / anonymous, session gone', async ({ browser }) => {
    const { context: signOutContext, page: signOutPage } = await signIn(browser, 'customer-a');
    await withGatewayCsp(signOutPage);
    await signOutPage.goto(`${spa}/transactions`);
    await expect(signOutPage.getByRole('button', { name: 'Sign out' })).toBeVisible();
    const cookieBefore = (await signOutContext.cookies()).find((cookie) => cookie.name === sessionCookieName)!;
    const sessionsBefore = Number(queryDatabase('select count(*) from "Sessions"'));

    const seen: string[] = [];
    let logoutBody = '';
    signOutPage.on('request', (request) => {
      if (request.url().includes('/bff/antiforgery')) seen.push('antiforgery');
      if (request.url().endsWith('/api/v1/bff/logout')) {
        seen.push(`${request.method()} logout navigation=${request.isNavigationRequest()}`);
        logoutBody = request.postData() ?? '';
      }
      if (request.url().includes('/oidc/v1/end_session')) seen.push('end_session');
    });
    const failure = signOutPage.waitForRequest((request) => request.url().includes('/oidc/v1/end_session'));
    await signOutPage.getByRole('button', { name: 'Sign out' }).click();
    await failure;
    await signOutPage.waitForURL(`${gateway}/`);
    expect(seen).toEqual(['antiforgery', 'POST logout navigation=true', 'end_session']);
    expect(logoutBody).toMatch(/^__RequestVerificationToken=[\w%-]{20,}$/);

    await signOutPage.goto(`${spa}/`);
    await expect(signOutPage.getByRole('heading', { name: /Something wrong with a card transaction/ })).toBeVisible();
    expect((await signOutContext.cookies()).some((cookie) => cookie.name === sessionCookieName)).toBe(false);
    expect(Number(queryDatabase('select count(*) from "Sessions"'))).toBe(sessionsBefore - 1);

    // The old cookie is dead on the server too.
    const replay = await playwrightRequest.newContext({ baseURL: gateway, extraHTTPHeaders: { cookie: `${cookieBefore.name}=${cookieBefore.value}` } });
    expect(await (await replay.get('/api/v1/bff/session')).json()).toEqual({ state: 'anonymous' });
    expect((await replay.get('/api/v1/transactions')).status()).toBe(401);
    await replay.dispose();
    await signOutContext.close();
  });

  test('a failed antiforgery fetch shows the unexpected-error alert and submits nothing', async ({ browser }) => {
    const { context: failContext, page: failPage } = await signIn(browser, 'customer-a');
    await failPage.goto(`${spa}/transactions`);
    await expect(failPage.getByRole('button', { name: 'Sign out' })).toBeVisible();
    await failPage.route('**/api/v1/bff/antiforgery', (route) => route.fulfill({ status: 503, contentType: 'application/problem+json', body: '{}' }));
    let logoutPosts = 0;
    failPage.on('request', (request) => {
      if (request.url().endsWith('/bff/logout')) logoutPosts += 1;
    });
    await failPage.getByRole('button', { name: 'Sign out' }).click();
    await expect(failPage.getByRole('alert')).toHaveText('Something went wrong on our side. Try again.');
    expect(logoutPosts).toBe(0);
    await failContext.close();
  });
});

test.describe('session ends while signed in', () => {
  test('a 401 on Load more goes to /signed-out?reason=expired&returnUrl, and Sign in again returns through the BFF', async ({ browser }) => {
    const { context, page } = await signIn(browser, 'customer-a');
    await page.goto(`${spa}/transactions`);
    await expect(page.locator('li')).toHaveCount(20);

    await endSessionOutsideThePage(context);

    await page.getByRole('button', { name: 'Load more' }).click();
    await expect(page).toHaveURL(`${spa}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
    await expect(page.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
    const signInAgain = page.getByRole('link', { name: 'Sign in again' });
    await expect(signInAgain).toHaveAttribute('href', '/api/v1/bff/login?returnUrl=%2Ftransactions');
    await expectNoAxeViolations(page);

    await signInAgain.click();
    const login = fixtureLogin('customer-a');
    // Zitadel may still hold its own session; fill the form only if it asks.
    const username = page.getByTestId('username-text-input');
    await Promise.race([username.waitFor({ timeout: 20_000 }).catch(() => undefined), page.waitForURL((url) => url.origin === gateway, { timeout: 20_000 }).catch(() => undefined)]);
    if (await username.isVisible()) {
      await username.fill(login.username);
      await username.press('Enter');
      await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
      await page.getByTestId('password-text-input').press('Enter');
    }
    // The BFF redirects to a local path on its own origin; the gateway serves the SPA from story 2.5.
    await page.waitForURL(`${gateway}/transactions`, { timeout: 60_000 });
    await context.close();
  });
});

test.describe('inactivity warning (Playwright clock)', () => {
  test('13 minutes: dialog; activity restarts the timer; Escape and Stay signed in renew; CSP console check', async ({ browser }) => {
    const { context, page } = await signIn(browser, 'customer-a');
    const consoleErrors: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') consoleErrors.push(message.text());
    });
    await withGatewayCsp(page);
    await page.clock.install();
    await page.goto(`${spa}/transactions`);
    await expect(page.locator('li')).toHaveCount(20);
    const dialog = page.getByRole('alertdialog');

    await page.clock.runFor('12:50');
    await expect(dialog).toHaveCount(0);
    await page.clock.runFor('00:20');
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText("You'll be signed out soon.");
    await expect(dialog).toContainText('For your security, we sign you out after 15 minutes without activity.');
    await expectNoAxeViolations(page);
    // Radix scroll lock injects a <style>; the gateway CSP (style-src 'self') blocks it (story 2.5 criterion 7, accepted
    // by decision). Any other console error, CSP or not, fails the test.
    expect(consoleErrors.filter((text) => !knownRadixStyleViolation.test(text))).toEqual([]);

    // Escape acts as "Stay signed in": one GET /session, then the dialog closes.
    let sessionCalls = 0;
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/v1/bff/session') sessionCalls += 1;
    });
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    expect(sessionCalls).toBe(1);

    // The timer restarted: nothing at 12:50 more, the dialog again after 13 minutes in all.
    await page.clock.runFor('12:50');
    await expect(dialog).toHaveCount(0);
    await page.clock.runFor('00:20');
    await expect(dialog).toBeVisible();
    await page.getByRole('button', { name: 'Stay signed in' }).click();
    await expect(dialog).toHaveCount(0);
    expect(sessionCalls).toBe(2);

    // Load more is an API response, so it restarts the timer too.
    await page.clock.runFor('12:00');
    const secondPage = page.waitForResponse((answer) => answer.url().includes('/api/v1/transactions?cursor='));
    await page.getByRole('button', { name: 'Load more' }).click();
    await expect(page.locator('li')).toHaveCount(20 + ((await (await secondPage).json()) as ApiPage).items.length);
    await page.clock.runFor('12:00');
    await expect(dialog).toHaveCount(0);
    await page.clock.runFor('01:10');
    await expect(dialog).toBeVisible();

    // Sign out in the dialog submits the logout form.
    const logout = page.waitForRequest((request) => request.url().endsWith('/api/v1/bff/logout') && request.method() === 'POST');
    await dialog.getByRole('button', { name: 'Sign out' }).click();
    await logout;
    await context.close();
  });

  test('Stay signed in after the session already ended (200 anonymous) goes to /signed-out?reason=expired', async ({ browser }) => {
    const { context, page } = await signIn(browser, 'customer-a');
    await page.clock.install();
    await page.goto(`${spa}/transactions`);
    await expect(page.locator('li')).toHaveCount(20);
    await endSessionOutsideThePage(context);

    await page.clock.runFor('13:01');
    await page.getByRole('button', { name: 'Stay signed in' }).click();
    await expect(page).toHaveURL(`${spa}/signed-out?reason=expired&returnUrl=%2Ftransactions`);
    await context.close();
  });
});

test.describe('transactions states', () => {
  let context: BrowserContext;
  let page: Page;
  test.beforeAll(async ({ browser }) => {
    ({ context, page } = await signIn(browser, 'customer-a'));
  });
  test.afterAll(async () => context.close());

  test('loading: skeleton with aria-busy while the response is slow, then the list', async () => {
    await page.route('**/api/v1/transactions*', async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 2500));
      await route.continue();
    });
    await page.goto(`${spa}/transactions`);
    await expect(page.locator('[aria-busy="true"]')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Transactions', level: 1 })).toBeVisible();
    await expectNoAxeViolations(page);
    await expect(page.locator('li').first()).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
    await page.unroute('**/api/v1/transactions*');
  });

  test('empty: the empty state (a stubbed empty page; no seeded customer has none)', async () => {
    await page.route('**/api/v1/transactions*', (route) => route.fulfill({ status: 200, contentType: 'application/json', body: '{"items":[],"nextCursor":null}' }));
    await page.goto(`${spa}/transactions`);
    await expect(page.getByText('No transactions in the last 90 days.')).toBeVisible();
    await expect(page.getByText('Card and account transactions appear here once they reach your account.')).toBeVisible();
    await expectNoAxeViolations(page);
    await page.unroute('**/api/v1/transactions*');
  });

  test('503 core-banking-unavailable with the container stopped: alert, one retry, Try again, View disputes', async () => {
    test.setTimeout(180_000);
    const transactionRequests: string[] = [];
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/v1/transactions') transactionRequests.push(request.url());
    });
    const traceIds: string[] = [];
    page.on('response', async (response) => {
      if (new URL(response.url()).pathname === '/api/v1/transactions' && response.status() === 503) traceIds.push((await response.json()).traceId);
    });
    execFileSync('docker', ['stop', coreBankingContainer]);
    try {
      const failed = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/transactions' && response.status() === 503);
      await page.goto(`${spa}/transactions`);
      const problem = await (await failed).json();
      expect(problem.type).toBe('https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/core-banking-unavailable.md');
      const alert = page.getByRole('alert');
      await expect(alert.getByText("We can't load your transactions right now.")).toBeVisible();
      await expect(alert.getByText('Your disputes are still available.')).toBeVisible();
      expect(transactionRequests).toHaveLength(2); // the read, and its one retry
      await expect.poll(() => traceIds.length).toBe(2);
      await expect(alert).toContainText(traceIds[1]); // the reference shown is the last response's traceId
      await expect(page.getByRole('link', { name: 'View disputes' })).toHaveAttribute('href', '/disputes');
      await expectNoAxeViolations(page);
      await page.setViewportSize({ width: 320, height: 800 });
      await expectNoHorizontalScroll(page);
      await page.setViewportSize({ width: 1280, height: 800 });

      await page.getByRole('button', { name: 'Try again' }).click();
      await expect.poll(() => transactionRequests.length).toBeGreaterThanOrEqual(3);
    } finally {
      execFileSync('docker', ['start', coreBankingContainer]);
      for (let attempt = 0; attempt < 40; attempt += 1) {
        const health = execFileSync('docker', ['inspect', '-f', '{{.State.Health.Status}}', coreBankingContainer], { encoding: 'utf8' }).trim();
        if (health === 'healthy') break;
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
    }
    // With the bank back, the list loads (the retry may already have done it, so load the screen afresh).
    await page.goto(`${spa}/transactions`);
    await expect(page.locator('li').first()).toBeVisible({ timeout: 30_000 });
  });
});
