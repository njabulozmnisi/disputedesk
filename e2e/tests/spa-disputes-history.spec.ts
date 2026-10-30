import AxeBuilder from '@axe-core/playwright';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { fixtureLogin, queryDatabase } from './support';

// Story 3.7: the Disputes history screen (/disputes) and the shell's skip link, through the real gateway, BFF, API and
// database, signed in as seeded customer A (B for the cross-customer check). The API can only produce `submitted` and
// `withdrawn` disputes today (the operations actions are not built), so the outcome badges, an unknown status and the
// empty, loading and failing states are shown by editing or stubbing the list response in the browser; the screen is real.
// Customer A has only 20 disputable transactions, so a second page cannot exist at the default page size of 20: the Load
// more test adds `limit=2` to the real request in the browser (the real API, cursor and rows), and a separate test
// proves the SPA itself sends no `limit`. The data is not reset between runs: the spec tops A up to three disputes
// (one withdrawn) and raises nothing else.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const knownRadixStyleViolation = /^Applying inline style violates the following Content Security Policy directive 'style-src 'self''\./;
const problemMessage = 'Something went wrong on our side. Try again.';
const reasonLabels: Record<string, string> = { unrecognised: 'Unrecognised', duplicateCharge: 'Duplicate charge', wrongAmount: 'Wrong amount', notReceived: 'Not received', cancelledSubscription: 'Cancelled subscription', refundNotProcessed: 'Refund not processed' };
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

type Item = {
  disputeId: string;
  customerStatus: string;
  outcome: string | null;
  reason: string;
  transaction: { description: string };
  disputedAmount: { amount: string; currency: string };
  createdAt: string;
};
type ApiPage = { items: Item[]; nextCursor: string | null };

const openStatuses = ['submitted', 'underReview', 'underReviewPrioritised'];
const closedStatuses = ['resolved', 'withdrawn'];
const statusLabels: Record<string, string> = {
  submitted: 'Submitted',
  underReview: 'Under review',
  underReviewPrioritised: 'Under review (prioritised)',
  withdrawn: 'Withdrawn',
  resolved: 'Resolved',
};

let customerA: BrowserContext;
let customerB: BrowserContext;

async function signIn(browser: Browser, loginKey: string): Promise<BrowserContext> {
  const login = fixtureLogin(loginKey);
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/api/v1/bff/login');
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  await page.close();
  return context;
}

async function axeViolations(page: Page) {
  const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  return result.violations.map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join('; ')}`);
}

function trackConsoleErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && !knownRadixStyleViolation.test(message.text())) errors.push(message.text());
  });
  return errors;
}

const squash = (text: string) => text.replace(/[\s ]+/g, ' ').trim();

/** South African time is UTC+2 all year, so "d Mon yyyy" is plain arithmetic. */
function expectedDay(isoDateTime: string): string {
  const johannesburg = new Date(new Date(isoDateTime).getTime() + 2 * 60 * 60 * 1000);
  return `${johannesburg.getUTCDate()} ${months[johannesburg.getUTCMonth()]} ${johannesburg.getUTCFullYear()}`;
}

function expectedAmount(item: Item): string {
  const [whole, cents] = item.disputedAmount.amount.split('.');
  return `R${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')}.${cents}`;
}

async function csrfToken(api: APIRequestContext): Promise<string> {
  return (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
}

async function listDisputes(api: APIRequestContext): Promise<Item[]> {
  const items: Item[] = [];
  let cursor = '';
  for (let guard = 0; guard < 50; guard += 1) {
    const response = await api.get(`/api/v1/disputes${cursor}`);
    expect(response.status()).toBe(200);
    const page = (await response.json()) as ApiPage;
    items.push(...page.items);
    if (!page.nextCursor) return items;
    cursor = `?cursor=${page.nextCursor}`;
  }
  throw new Error('the dispute list did not end');
}

/** Raises on the first disputable transaction left; returns the new dispute's ID, or undefined when none is left. */
async function raiseOne(api: APIRequestContext): Promise<string | undefined> {
  const list = await api.get('/api/v1/transactions?limit=50');
  const row = ((await list.json()).items as { transactionId: string; accountRef: string; disputable: boolean }[]).find((item) => item.disputable);
  if (!row) return undefined;
  const created = await api.post('/api/v1/disputes', {
    headers: { 'x-xsrf-token': await csrfToken(api), 'idempotency-key': `"${crypto.randomUUID()}"` },
    data: { accountRef: row.accountRef, transactionId: row.transactionId, reason: 'unrecognised', note: 'qa 3.7' },
  });
  expect(created.status()).toBe(201);
  return ((await created.json()) as { disputeId: string }).disputeId;
}

async function withdraw(api: APIRequestContext, disputeId: string) {
  const response = await api.post(`/api/v1/disputes/${disputeId}/withdraw`, {
    headers: { 'x-xsrf-token': await csrfToken(api), 'content-type': 'application/json' },
    data: '{}',
    maxRedirects: 0,
  });
  expect(response.status()).toBe(200);
}

/** The visible rows of a section, in document order, with their link names and targets. */
async function sectionRows(page: Page, heading: string) {
  const section = page.locator('section', { has: page.getByRole('heading', { level: 2, name: heading, exact: true }) });
  const rows = section.locator('li');
  const count = await rows.count();
  const result: { href: string | null; name: string; text: string }[] = [];
  for (let index = 0; index < count; index += 1) {
    const link = rows.nth(index).getByRole('link');
    result.push({ href: await link.getAttribute('href'), name: squash(await link.evaluate((element) => element.textContent ?? '')), text: squash(await rows.nth(index).innerText()) });
  }
  return result;
}

/** Fulfils the first matching list request from the real API with the edited body. */
async function editList(page: Page, edit: (body: ApiPage) => void) {
  await page.route('**/api/v1/disputes', async (route) => {
    const response = await route.fetch();
    const body = (await response.json()) as ApiPage;
    edit(body);
    await route.fulfill({ response, json: body });
  });
}

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => {
  customerA = await signIn(browser, 'customer-a');
  customerB = await signIn(browser, 'customer-b');
  let items = await listDisputes(customerA.request);
  while (items.length < 3) {
    if (!(await raiseOne(customerA.request))) break;
    items = await listDisputes(customerA.request);
  }
  if (!items.some((item) => item.customerStatus === 'withdrawn')) {
    const open = items.find((item) => item.customerStatus === 'submitted');
    const disputeId = items.length < 6 ? await raiseOne(customerA.request) : open?.disputeId;
    if (disputeId) await withdraw(customerA.request, disputeId);
  }
});
test.afterAll(async () => {
  await customerA.close().catch(() => undefined);
  await customerB.close().catch(() => undefined);
});

test('criterion 1: Open and Closed in API order; rows match the API and the database; the SPA sends no limit; no other customer data', async () => {
  const page = await customerA.newPage();
  await page.setViewportSize({ width: 1280, height: 800 });
  const consoleErrors = trackConsoleErrors(page);
  const dataRequests: string[] = [];
  page.on('request', (request) => {
    if (request.resourceType() === 'fetch' || request.resourceType() === 'xhr') dataRequests.push(`${request.method()} ${new URL(request.url()).pathname}${new URL(request.url()).search}`);
  });
  const answered = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/disputes');
  await page.goto('/disputes');
  const response = await answered;

  // Network: GET /api/v1/disputes with no query (no limit, no cursor), 200, the contract's fields only, no cache.
  expect(response.request().method()).toBe('GET');
  expect(new URL(response.url()).search).toBe('');
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toMatch(/no-store/);
  const body = (await response.json()) as ApiPage;
  expect(body.items.length).toBeGreaterThanOrEqual(2);
  for (const item of body.items) expect(Object.keys(item).sort()).toEqual(['createdAt', 'customerStatus', 'disputeId', 'disputedAmount', 'outcome', 'reason', 'transaction']);
  expect(dataRequests.filter((request) => !/ \/api\/v1\/(disputes|bff\/session)(\?|$)/.test(request))).toEqual([]);

  // Browser: heading, subtitle, sections.
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes', exact: true })).toBeVisible();
  await expect(page.getByText("Every dispute you've raised, newest first.")).toBeVisible();
  await expect(page).toHaveTitle('Disputes · DisputeDesk');
  const expectedOpen = body.items.filter((item) => openStatuses.includes(item.customerStatus));
  const expectedClosed = body.items.filter((item) => closedStatuses.includes(item.customerStatus));
  expect(expectedOpen.length).toBeGreaterThan(0);
  expect(expectedClosed.length).toBeGreaterThan(0);
  const shownOpen = await sectionRows(page, 'Open');
  const shownClosed = await sectionRows(page, 'Closed');
  expect(shownOpen.map((row) => row.href)).toEqual(expectedOpen.map((item) => `/disputes/${item.disputeId}`));
  expect(shownClosed.map((row) => row.href)).toEqual(expectedClosed.map((item) => `/disputes/${item.disputeId}`));
  // Newest first within each section (the API order is createdAt descending).
  for (const group of [expectedOpen, expectedClosed]) {
    const times = group.map((item) => Date.parse(item.createdAt));
    expect(times).toEqual([...times].sort((left, right) => right - left));
  }

  // Each row: description, reason label, "raised d Mon yyyy", amount, status badge; the link name carries description and status.
  for (const [rows, group] of [[shownOpen, expectedOpen], [shownClosed, expectedClosed]] as const) {
    for (const [index, item] of group.entries()) {
      const row = rows[index];
      expect(row.text).toContain(item.transaction.description);
      expect(row.text).toContain(`${reasonLabels[item.reason]}, raised ${expectedDay(item.createdAt)}`);
      expect(row.text).toContain(expectedAmount(item));
      expect(row.name).toContain(item.transaction.description);
      expect(row.name).toContain(`Dispute status: ${statusLabels[item.customerStatus]}`);
      expect(row.text).not.toContain(item.disputeId);
    }
  }
  const body$ = squash(await page.locator('body').innerText());
  expect(body$).not.toMatch(/escalated/i);
  expect(await page.locator('body').innerHTML()).not.toMatch(/escalated/i);
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length, document.cookie])).toEqual([0, 0, '']);
  expect(await page.evaluate(() => location.search + location.hash)).toBe('');
  expect(await axeViolations(page), 'axe at 1280').toEqual([]);
  await page.setViewportSize({ width: 320, height: 800 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
  expect(await axeViolations(page), 'axe at 320').toEqual([]);

  // Database (read-only role): every listed dispute is a stored row of one customer, with the status, description,
  // amount and instant the screen shows; the customer has no stored dispute the list left out.
  const customerIds = queryDatabase(`select distinct "CustomerId" from "Disputes" where "Id" in (${body.items.map((item) => `'${item.disputeId}'`).join(',')})`).split('\n');
  expect(customerIds).toHaveLength(1);
  const stored = queryDatabase(`select "Id", "Status", "Description", "DisputedAmount"::text, to_char("CreatedAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI') from "Disputes" where "CustomerId" = '${customerIds[0]}'`)
    .split('\n').map((line) => line.split('|'));
  expect(stored.map((row) => row[0]).sort()).toEqual(body.items.map((item) => item.disputeId).sort());
  for (const item of body.items) {
    const row = stored.find((candidate) => candidate[0] === item.disputeId)!;
    expect(row[1]).toBe(item.customerStatus === 'submitted' ? 'Submitted' : item.customerStatus === 'withdrawn' ? 'Withdrawn' : row[1]);
    expect(row[2]).toBe(item.transaction.description);
    expect(row[3]).toBe(item.disputedAmount.amount);
    expect(item.createdAt.startsWith(row[4])).toBe(true);
  }

  // Another customer's disputes are not in A's list, and A cannot open one: 404, no data (the detail screen is 3.12).
  const bItems = await listDisputes(customerB.request);
  const aIds = new Set(body.items.map((item) => item.disputeId));
  expect(bItems.filter((item) => aIds.has(item.disputeId))).toEqual([]);
  expect(body$).not.toContain('qa 3.7 B');
  for (const other of bItems.slice(0, 2)) {
    expect(body$).not.toContain(other.disputeId);
    const foreign = await customerA.request.get(`/api/v1/disputes/${other.disputeId}`);
    expect(foreign.status()).toBe(404);
    expect(JSON.stringify(await foreign.json())).not.toContain(other.transaction.description);
  }
  expect(consoleErrors).toEqual([]);
  await page.close();
});

test('criterion 2: outcome badges, and an unknown status or outcome (response edited in the browser)', async () => {
  const page = await customerA.newPage();
  const consoleErrors = trackConsoleErrors(page);
  let edited!: ApiPage;
  await editList(page, (body) => {
    const templates = body.items.slice(0, 1);
    const make = (id: string, patch: Partial<Item>): Item => ({ ...templates[0], disputeId: id, transaction: { description: `Edited ${id.slice(0, 4)}` }, ...patch });
    body.items = [
      make('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', { customerStatus: 'resolved', outcome: 'upheld' }),
      make('bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', { customerStatus: 'resolved', outcome: 'rejected' }),
      make('cccccccc-cccc-4ccc-8ccc-cccccccccccc', { customerStatus: 'resolved', outcome: null }),
      make('dddddddd-dddd-4ddd-8ddd-dddddddddddd', { customerStatus: 'resolved', outcome: 'partially' }),
      make('eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee', { customerStatus: 'underReview', outcome: 'upheld' }),
      make('ffffffff-ffff-4fff-8fff-ffffffffffff', { customerStatus: 'escalated' }),
      make('11111111-1111-4111-8111-111111111111', { customerStatus: 'underReviewPrioritised', reason: 'something-new' }),
      make('22222222-2222-4222-8222-222222222222', { customerStatus: 'withdrawn' }),
    ];
    edited = body;
  });
  await page.goto('/disputes');
  await expect(page.getByRole('heading', { level: 2, name: 'Closed', exact: true })).toBeVisible();
  const closed = await sectionRows(page, 'Closed');
  const open = await sectionRows(page, 'Open');
  expect(closed.map((row) => row.name.match(/Dispute status: (.*)$/)?.[1])).toEqual(['Resolved · Upheld', 'Resolved · Rejected', 'Resolved', 'Resolved', 'Withdrawn']);
  expect(open.map((row) => row.name.match(/Dispute status: (.*)$/)?.[1])).toEqual(['Under review', 'Under review (prioritised)']);
  // 'escalated' is left out of both groups; an unknown reason has no label, never the raw value.
  expect(edited.items).toHaveLength(8);
  expect(closed.length + open.length).toBe(7);
  const text = squash(await page.locator('body').innerText());
  expect(text).not.toMatch(/escalated|something-new|partially/i);
  expect(open[1].text).toMatch(/Raised \d/);
  // Tone and icon: success and danger tones carry a check and a cross, on the existing status tokens.
  const upheld = page.getByRole('link', { name: /Resolved · Upheld/ }).locator('span').filter({ hasText: '✓' }).first();
  const rejected = page.getByRole('link', { name: /Resolved · Rejected/ }).locator('span').filter({ hasText: '✕' }).first();
  await expect(upheld).toBeVisible();
  await expect(rejected).toBeVisible();
  expect(await axeViolations(page), 'axe with outcome badges').toEqual([]);
  expect(consoleErrors).toEqual([]);
  await page.close();
});

test('criterion 3: Load more by cursor, Loading… not pressable twice, focus on the first new dispute by ID, announced, gone at the end', async () => {
  const total = (await listDisputes(customerA.request)).length;
  expect(total).toBeGreaterThanOrEqual(3);
  const page = await customerA.newPage();
  const consoleErrors = trackConsoleErrors(page);
  // The real list with a page size of 2; the SPA's own request is checked in the first test to carry no limit.
  const seen: { query: string; body: ApiPage }[] = [];
  let hold: (() => void) | undefined;
  const listRequests: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/v1/disputes') listRequests.push(new URL(request.url()).search);
  });
  await page.route(/\/api\/v1\/disputes(\?.*)?$/, async (route) => {
    const url = new URL(route.request().url());
    url.searchParams.set('limit', '2');
    const response = await route.fetch({ url: url.toString() });
    const body = (await response.json()) as ApiPage;
    seen.push({ query: new URL(route.request().url()).search, body });
    if (seen.length === 2) await new Promise<void>((release) => { hold = release; setTimeout(release, 10_000); });
    await route.fulfill({ response, json: body });
  });
  await page.goto('/disputes');
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  const loadMore = page.getByRole('button', { name: 'Load more' });
  await expect(loadMore).toBeVisible();
  expect(seen).toHaveLength(1);
  expect(seen[0].body.items).toHaveLength(2);
  const firstCursor = seen[0].body.nextCursor;
  expect(firstCursor).toBeTruthy();
  const idsBefore = new Set(seen[0].body.items.map((item) => item.disputeId));

  // Press once, then again while loading: "Loading…" is focusable with aria-disabled, and no second request goes out.
  await loadMore.click();
  const loading = page.getByRole('button', { name: 'Loading…' });
  await expect(loading).toHaveAttribute('aria-disabled', 'true');
  await loading.focus();
  await expect(loading).toBeFocused();
  await loading.click({ force: true });
  await page.keyboard.press('Enter');
  await expect.poll(() => seen.length).toBe(2);
  await page.waitForTimeout(500); // the second request is held, so a second press would show up as a third request here
  expect(listRequests).toHaveLength(2);
  expect(seen).toHaveLength(2);
  expect(seen[1].query).toBe(`?cursor=${firstCursor}`); // the cursor comes back unchanged
  hold?.();

  // The new page: focus is on the first new dispute in API order, found by its ID (its row li has tabindex -1).
  await expect(page.locator('main li')).toHaveCount(idsBefore.size + seen[1].body.items.length);
  await expect(page.locator('[aria-live="polite"]')).not.toHaveText('');
  const secondBody = seen[1].body;
  const firstNew = secondBody.items.find((item) => !idsBefore.has(item.disputeId))!;
  const focusedHref = await page.evaluate(() => document.activeElement?.querySelector('a')?.getAttribute('href') ?? null);
  expect(focusedHref).toBe(`/disputes/${firstNew.disputeId}`);
  expect(await page.evaluate(() => document.activeElement?.tagName + ':' + document.activeElement?.getAttribute('tabindex'))).toBe('LI:-1');
  const newCount = secondBody.items.filter((item) => !idsBefore.has(item.disputeId)).length;
  await expect(page.locator('[aria-live="polite"]')).toHaveText(`${newCount} more ${newCount === 1 ? 'dispute' : 'disputes'} loaded`);

  // Keep pressing until the last page: the button goes, every dispute is shown once, in API order within each section.
  while (await loadMore.count()) {
    const before = await page.locator('main li').count();
    await loadMore.click();
    await expect.poll(() => page.locator('main li').count()).toBeGreaterThan(before);
  }
  await expect(page.locator('main li')).toHaveCount(total);
  const allIds = [...seen.flatMap((entry) => entry.body.items.map((item) => item.disputeId))];
  expect(new Set(allIds).size).toBe(allIds.length);
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length, location.search + location.hash])).toEqual([0, 0, '']);
  expect(await axeViolations(page), 'axe after Load more').toEqual([]);
  expect(consoleErrors).toEqual([]);
  await page.close();
});

test('criterion 3: a failed Load more keeps the rows and the button and shows the notice with a reference, never title or detail', async () => {
  const page = await customerA.newPage();
  let calls = 0;
  await page.route(/\/api\/v1\/disputes(\?.*)?$/, async (route) => {
    calls += 1;
    if (calls === 1) {
      const url = new URL(route.request().url());
      url.searchParams.set('limit', '2');
      return route.continue({ url: url.toString() });
    }
    return route.fulfill({
      status: 500,
      contentType: 'application/problem+json',
      body: JSON.stringify({ type: 'about:blank', title: 'SECRET-TITLE', detail: 'SECRET-DETAIL', status: 500, traceId: 'trace-qa-37' }),
    });
  });
  await page.goto('/disputes');
  await expect(page.getByRole('button', { name: 'Load more' })).toBeVisible();
  const rowsBefore = await page.locator('main li').count();
  await page.getByRole('button', { name: 'Load more' }).click();
  await expect(page.getByText(problemMessage)).toBeVisible();
  await expect(page.getByText(/trace-qa-37/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Load more' })).toBeVisible();
  await expect(page.locator('main li')).toHaveCount(rowsBefore);
  expect(await page.locator('body').innerText()).not.toMatch(/SECRET-/);
  await page.close();
});

test('criterion 4: loading skeleton, empty state, error with Try again, and the list works with core banking down', async () => {
  // Loading: the h1 at once, 5 busy skeleton rows.
  const loadingPage = await customerA.newPage();
  let release!: () => void;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  await loadingPage.route('**/api/v1/disputes', async (route) => { await gate; await route.continue(); });
  await loadingPage.goto('/disputes');
  await expect(loadingPage.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  const skeleton = loadingPage.getByRole('list', { name: 'Loading disputes' });
  await expect(skeleton).toHaveAttribute('aria-busy', 'true');
  await expect(skeleton.locator('li')).toHaveCount(5);
  expect(await axeViolations(loadingPage), 'axe while loading').toEqual([]);
  release();
  await expect(loadingPage.getByRole('list', { name: 'Loading disputes' })).toHaveCount(0);
  await loadingPage.close();

  // Empty.
  const emptyPage = await customerA.newPage();
  await emptyPage.route('**/api/v1/disputes', (route) => route.fulfill({ json: { items: [], nextCursor: null } }));
  await emptyPage.goto('/disputes');
  await expect(emptyPage.getByRole('heading', { level: 2, name: "You haven't disputed any transactions." })).toBeVisible();
  await expect(emptyPage.getByText('If something on your account looks wrong, find it in your transactions and choose Dispute. It will show here with its status.')).toBeVisible();
  await expect(emptyPage.getByRole('heading', { level: 2, name: /^(Open|Closed)$/ })).toHaveCount(0);
  expect(await axeViolations(emptyPage), 'axe empty').toEqual([]);
  await emptyPage.getByRole('link', { name: 'Go to transactions' }).click();
  await expect(emptyPage).toHaveURL(`${origin}/transactions`);
  await emptyPage.close();

  // Failure (5xx and network): the notice with the trace reference, never the problem's title or detail; Try again refetches.
  for (const failure of ['5xx', 'network'] as const) {
    const errorPage = await customerA.newPage();
    let calls = 0;
    await errorPage.route('**/api/v1/disputes', (route) => {
      calls += 1;
      if (calls <= 2) {
        return failure === '5xx'
          ? route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ title: 'SECRET-TITLE', detail: 'SECRET-DETAIL', status: 503, traceId: 'trace-qa-37-list' }) })
          : route.abort('failed');
      }
      return route.continue();
    });
    await errorPage.goto('/disputes');
    await expect(errorPage.getByText(problemMessage)).toBeVisible();
    if (failure === '5xx') await expect(errorPage.getByText(/trace-qa-37-list/)).toBeVisible();
    expect(await errorPage.locator('body').innerText()).not.toMatch(/SECRET-/);
    expect(await axeViolations(errorPage), `axe ${failure} error`).toEqual([]);
    await errorPage.getByRole('button', { name: 'Try again' }).click();
    await expect(errorPage.getByRole('heading', { level: 2, name: /^(Open|Closed)$/ }).first()).toBeVisible();
    await expect(errorPage.getByText(problemMessage)).toHaveCount(0);
    await errorPage.close();
  }
});

test('criterion 5: typed links to Disputes make no document load; the current item is marked aria-current', async () => {
  const page = await customerA.newPage();
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/transactions');
  await expect(page.locator('li').first()).toBeVisible();
  await page.evaluate(() => { (window as unknown as { qaMarker: string }).qaMarker = 'same-document'; });
  await expect(page.getByRole('link', { name: 'Transactions', exact: true }).first()).toHaveAttribute('aria-current', 'page');
  await page.getByRole('banner').getByRole('link', { name: 'Disputes', exact: true }).click();
  await expect(page).toHaveURL(`${origin}/disputes`);
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { qaMarker?: string }).qaMarker)).toBe('same-document');
  await expect(page.getByRole('banner').getByRole('link', { name: 'Disputes', exact: true })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('banner').getByRole('link', { name: 'Transactions', exact: true })).not.toHaveAttribute('aria-current', 'page');

  // The phone tab bar.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('link', { name: /Transactions/ }).last().click();
  await expect(page.getByRole('heading', { level: 1, name: 'Transactions' })).toBeVisible();
  await page.getByRole('link', { name: /Disputes/ }).last().click();
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { qaMarker?: string }).qaMarker)).toBe('same-document');

  // The not-found screen's shortcut.
  await page.goto('/nope');
  await page.evaluate(() => { (window as unknown as { qaMarker: string }).qaMarker = 'same-document'; });
  await page.getByRole('link', { name: 'Go to your disputes' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { qaMarker?: string }).qaMarker)).toBe('same-document');
  await page.close();
});

test('criterion 6: the first Tab on a page load reaches the skip link; Enter and click move focus to main; a route change focuses the h1', async () => {
  const page = await customerA.newPage();
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/disputes');
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  // The first screen of a page load leaves focus alone.
  expect(await page.evaluate(() => document.activeElement === document.body)).toBe(true);
  // It is the first focusable element in document order, and hidden until focused.
  const order = await page.evaluate(() => {
    const focusable = Array.from(document.querySelectorAll<HTMLElement>('a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])'));
    return focusable[0].textContent;
  });
  expect(order).toBe('Skip to main content');
  const skip = page.getByRole('link', { name: 'Skip to main content' });
  const hiddenBox = await skip.boundingBox();
  expect(hiddenBox === null || hiddenBox.width <= 1).toBe(true);

  await page.keyboard.press('Tab');
  await expect(skip).toBeFocused();
  const box = (await skip.boundingBox())!;
  expect(box.width).toBeGreaterThan(40);
  expect(box.x).toBeGreaterThanOrEqual(0);
  expect(box.y).toBeGreaterThanOrEqual(0);
  const outline = await skip.evaluate((element) => getComputedStyle(element).outlineStyle + '|' + getComputedStyle(element).boxShadow);
  expect(outline).not.toBe('none|none');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'DisputeDesk' })).toBeFocused();

  // Enter: focus on <main id="main" tabindex="-1">, no hash navigation, still on /disputes.
  await page.keyboard.press('Shift+Tab');
  await expect(skip).toBeFocused();
  await page.keyboard.press('Enter');
  expect(await page.evaluate(() => [document.activeElement?.id, document.activeElement?.tagName, document.activeElement?.getAttribute('tabindex'), location.hash])).toEqual(['main', 'MAIN', '-1', '']);
  await expect(page).toHaveURL(`${origin}/disputes`);
  // The next Tab goes into the main content, past the header.
  await page.keyboard.press('Tab');
  expect(await page.evaluate(() => document.activeElement?.closest('main') !== null)).toBe(true);

  // Click: the same.
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(skip).toBeFocused();
  await skip.click();
  expect(await page.evaluate(() => [document.activeElement?.id, location.hash])).toEqual(['main', '']);

  // A route change focuses the new screen's h1, which is inside main (past the header).
  await page.getByRole('banner').getByRole('link', { name: 'Transactions', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Transactions' })).toBeFocused();
  expect(await page.evaluate(() => document.activeElement?.closest('main') !== null)).toBe(true);
  await page.getByRole('banner').getByRole('link', { name: 'Disputes', exact: true }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeFocused();

  // Reload on another screen: the first Tab still starts at the skip link there.
  await page.goto('/transactions');
  await expect(page.getByRole('heading', { level: 1, name: 'Transactions' })).toBeVisible();
  expect(await page.evaluate(() => document.activeElement === document.body)).toBe(true);
  await page.keyboard.press('Tab');
  await expect(skip).toBeFocused();
  await page.close();
});

test('criterion 4: the list works with core banking stopped (stored snapshots, no 503 path)', async () => {
  const container = 'disputedesk-core-banking-1';
  const page = await customerA.newPage();
  execFileSync('docker', ['stop', container]);
  try {
    const answered = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/disputes');
    await page.goto('/disputes');
    expect((await answered).status()).toBe(200);
    await expect(page.getByRole('heading', { level: 2, name: /^(Open|Closed)$/ }).first()).toBeVisible();
    await expect(page.getByText(problemMessage)).toHaveCount(0);
  } finally {
    execFileSync('docker', ['start', container]);
    await page.close();
  }
});

test('an unlinked customer (C) does not get the Disputes screen or any dispute data', async ({ browser }) => {
  const customerC = await signIn(browser, 'customer-c');
  const page = await customerC.newPage();
  const listed: number[] = [];
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/api/v1/disputes') listed.push(response.status());
  });
  await page.goto('/disputes');
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toHaveCount(0);
  await expect(page.getByRole('heading', { level: 2, name: /^(Open|Closed)$/ })).toHaveCount(0);
  expect(listed).toEqual([]);
  expect((await customerC.request.get('/api/v1/disputes')).status()).toBe(403);
  await customerC.close();
});
