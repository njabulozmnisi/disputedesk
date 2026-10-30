import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { fixtureLogin, ownDisputable, queryDatabase, topicRecords } from './support';

// Story 3.10: dispute status badges on transaction rows, through the real gateway, BFF, API, database and topic,
// signed in as seeded customer A. The API can only produce `submitted` today (withdraw and the operations actions are
// not built), so the other four statuses, the out-of-contract 'escalated' and the half-set pair are shown by changing
// the transactions response in the browser (route.fetch, then edit the first rows); the row layout, the badge and
// the href are the real SPA. The list is not stubbed in the first test.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const knownRadixStyleViolation = /^Applying inline style violates the following Content Security Policy directive 'style-src 'self''\./;
const screenshotDir = process.env.QA_SCREENSHOT_DIR;

type ApiRow = {
  transactionId: string;
  description: string;
  accountRef: string;
  disputable: boolean;
  notDisputableReason: string | null;
  disputeId?: string | null;
  customerStatus?: string | null;
};
type ApiPage = { items: ApiRow[]; nextCursor: string | null };

let customerA: BrowserContext;

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

const bodyText = async (page: Page) => (await page.locator('body').innerText()).replace(/\s+/g, ' ');
const hintPhrases = /Pending\. |Money in\. |Transfer between|Not a card payment|Older than 90 days|Not disputable|Contact the bank/;

function trackConsoleErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && !knownRadixStyleViolation.test(message.text())) errors.push(message.text());
  });
  return errors;
}

/** The paths of the requests the page makes with fetch or XHR after it is told to start recording. */
function trackDataRequests(page: Page): string[] {
  const paths: string[] = [];
  page.on('request', (request) => {
    if (request.resourceType() === 'fetch' || request.resourceType() === 'xhr') paths.push(`${request.method()} ${new URL(request.url()).pathname}`);
  });
  return paths;
}

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => {
  customerA = await signIn(browser, 'customer-b');
});
test.afterAll(async () => customerA.close().catch(() => undefined));

test('a raised dispute shows a Submitted badge link on its row; stored event, outbox row and topic record agree', async () => {
  const page = await customerA.newPage();
  const consoleErrors = trackConsoleErrors(page);
  const listResponse = await customerA.request.get('/api/v1/transactions?limit=50');
  const before = ((await listResponse.json()) as ApiPage).items;
  const target = ownDisputable(before, 'spa-dispute-status-badges', 'b')[0];
  expect(target, 'the spec\'s reserved customer B transaction is disputable (reservedTransactions in support.ts)').toBeTruthy();

  // Raise through the API with the BFF's antiforgery token, as the SPA does (RFC 9651 quoted idempotency key).
  const token = (await (await customerA.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const created = await customerA.request.post('/api/v1/disputes', {
    headers: { 'x-xsrf-token': token, 'idempotency-key': `"${crypto.randomUUID()}"` },
    data: { accountRef: target!.accountRef, transactionId: target!.transactionId, reason: 'unrecognised', note: 'qa 3.10', expectedAmount: null },
  });
  expect(created.status()).toBe(201);
  const { disputeId } = (await created.json()) as { disputeId: string };

  // Network: the list names this dispute on exactly that row, and no other row of the page changed.
  const transactionsResponse = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/transactions');
  const dataRequests = trackDataRequests(page);
  await page.goto('/transactions');
  const body = (await (await transactionsResponse).json()) as ApiPage;
  const apiRow = body.items.find((row) => row.transactionId === target!.transactionId)!;
  expect(apiRow).toMatchObject({ disputeId, customerStatus: 'submitted', disputable: false, notDisputableReason: 'alreadyDisputed' });
  expect(Object.keys(apiRow)).not.toContain('outcome');

  // Browser: that row has exactly one link, the badge, named "Dispute status: Submitted", to /disputes/<id>.
  const rows = page.locator('li');
  await expect(rows).toHaveCount(body.items.length);
  const li = rows.nth(body.items.findIndex((row) => row.transactionId === target!.transactionId));
  await expect(li.getByRole('link')).toHaveCount(1);
  const badge = li.getByRole('link', { name: 'Dispute status: Submitted', exact: true });
  await expect(badge).toHaveAttribute('href', `/disputes/${encodeURIComponent(disputeId)}`);
  expect((await li.innerText()).replace(/\s+/g, ' ')).not.toMatch(hintPhrases);
  expect(await li.innerText()).not.toContain(disputeId);
  expect(await badge.getAttribute('aria-label')).toBeNull();
  expect(await page.locator('body').innerText()).not.toMatch(/escalated/i);

  // Every other row of the list: a Dispute link exactly when disputable, a badge link exactly when it has a dispute.
  for (const [index, row] of body.items.entries()) {
    await expect(rows.nth(index).getByRole('link', { name: 'Dispute this transaction' }), row.transactionId).toHaveCount(row.disputable ? 1 : 0);
    await expect(rows.nth(index).getByRole('link', { name: /^Dispute status: / }), row.transactionId).toHaveCount(row.disputeId ? 1 : 0);
  }
  // Only the transactions list (and the session check) is requested: no dispute request, no preload.
  await badge.hover();
  await badge.focus();
  expect(dataRequests.filter((request) => !/ \/api\/v1\/(transactions|bff\/session)$/.test(request))).toEqual([]);
  expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
  expect(await axeViolations(page), 'real list axe').toEqual([]);

  // Story 3.12: the real badge opens the real detail (the transaction's description as the one h1, Submitted, Withdraw offered).
  const detailResponse = page.waitForResponse((response) => new URL(response.url()).pathname === `/api/v1/disputes/${disputeId}`);
  await badge.click();
  expect((await detailResponse).status()).toBe(200);
  await expect(page).toHaveURL(`${origin}/disputes/${disputeId}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(target!.description);
  await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toBeVisible();
  await expect(page.getByRole('heading', { name: "We can't find that dispute" })).toHaveCount(0);
  await page.goBack();
  await expect(page.locator('li')).toHaveCount(body.items.length);

  // Database (read-only role): the dispute, its stored event and its outbox row, one ID.
  const stored = queryDatabase(`select d."Status", e."EventType", o."EventType", o."Id" = e."Id" from "Disputes" d join "StoredEvents" e on e."DisputeId" = d."Id" join "OutboxMessages" o on o."DisputeId" = d."Id" where d."Id" = '${disputeId}'`);
  expect(stored).toBe('Submitted|DisputeRaised|disputedesk.dispute.raised.v1|t');
  // Events: the relay publishes it with the dispute ID as the key and trace context in the headers.
  await expect
    .poll(() => topicRecords().filter((record) => record.value.includes(disputeId)).length, { timeout: 60_000 })
    .toBeGreaterThanOrEqual(1);
  const record = topicRecords().find((candidate) => candidate.value.includes(disputeId))!;
  expect(record.key).toBe(disputeId);
  expect(record.headers.map((header) => header.key)).toContain('traceparent');
  expect(consoleErrors).toEqual([]);
  await page.close();
});

test.describe('every status, an unknown one and a half-set pair (response edited in the browser)', () => {
  const disputeIds = {
    submitted: '11111111-1111-4111-8111-111111111111',
    underReview: '22222222-2222-4222-8222-222222222222',
    underReviewPrioritised: '33333333-3333-4333-8333-333333333333',
    resolved: '44444444-4444-4444-8444-444444444444',
    withdrawn: '../x?y#z', // out of contract (a uuid), only to prove the href is encoded
  };
  const labels: Record<string, string> = {
    submitted: 'Submitted',
    underReview: 'Under review',
    underReviewPrioritised: 'Under review (prioritised)',
    resolved: 'Resolved',
    withdrawn: 'Withdrawn',
  };
  const statuses = Object.keys(disputeIds) as (keyof typeof disputeIds)[];

  async function openEdited(page: Page): Promise<ApiPage> {
    let edited!: ApiPage;
    await page.route('**/api/v1/transactions*', async (route) => {
      const response = await route.fetch();
      const body = (await response.json()) as ApiPage;
      const alreadyDisputed = { disputable: false, notDisputableReason: 'alreadyDisputed' };
      statuses.forEach((status, index) => Object.assign(body.items[index], alreadyDisputed, { disputeId: disputeIds[status], customerStatus: status }));
      Object.assign(body.items[5], alreadyDisputed, { disputeId: '55555555-5555-4555-8555-555555555555', customerStatus: 'escalated' });
      Object.assign(body.items[6], alreadyDisputed, { disputeId: '66666666-6666-4666-8666-666666666666', customerStatus: null });
      Object.assign(body.items[7], alreadyDisputed, { disputeId: null, customerStatus: 'resolved' });
      edited = body;
      await route.fulfill({ response, json: body });
    });
    const answered = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/v1/transactions');
    await page.goto('/transactions');
    await answered;
    await expect(page.getByRole('link', { name: /^Dispute status: / }).first()).toBeVisible();
    return edited;
  }

  test('1280 px: one badge link per disputed row, no Escalated, plain rows unchanged, only the list is requested, axe clean', async () => {
    const page = await customerA.newPage();
    await page.setViewportSize({ width: 1280, height: 800 });
    const consoleErrors = trackConsoleErrors(page);
    const dataRequests = trackDataRequests(page);
    const body = await openEdited(page);
    const rows = page.locator('li');

    for (const [index, status] of statuses.entries()) {
      const li = rows.nth(index);
      await expect(li.getByRole('link')).toHaveCount(1);
      const badge = li.getByRole('link', { name: `Dispute status: ${labels[status]}`, exact: true });
      await expect(badge).toHaveAttribute('href', `/disputes/${encodeURIComponent(disputeIds[status])}`);
      expect((await li.innerText()).replace(/\s+/g, ' ')).not.toMatch(hintPhrases);
      expect(await li.innerText()).not.toContain(disputeIds[status]);
    }
    await expect(rows.nth(4).getByRole('link')).toHaveAttribute('href', '/disputes/..%2Fx%3Fy%23z');

    // 'escalated' and a half-set pair: no badge, no Dispute control, no hint.
    for (const index of [5, 6, 7]) {
      await expect(rows.nth(index).getByRole('link')).toHaveCount(0);
      expect((await rows.nth(index).innerText()).replace(/\s+/g, ' ')).not.toMatch(hintPhrases);
    }
    expect(await page.locator('body').innerText()).not.toMatch(/escalated/i);
    expect(await page.locator('body').innerHTML()).not.toMatch(/escalated/i);

    // Rows past the edited ones are as the API sent them.
    for (const [index, row] of body.items.entries()) {
      if (index < 8) continue;
      await expect(rows.nth(index).getByRole('link', { name: 'Dispute this transaction' })).toHaveCount(row.disputable ? 1 : 0);
      await expect(rows.nth(index).getByRole('link', { name: /^Dispute status: / })).toHaveCount(row.disputeId ? 1 : 0);
    }
    expect(dataRequests.filter((request) => !/ \/api\/v1\/(transactions|bff\/session)$/.test(request))).toEqual([]);
    expect(await axeViolations(page), 'axe at 1280').toEqual([]);

    // From 768px the badge is in the action column: right of the row text, right edges aligned with the amount's.
    const first = rows.first();
    const textBox = (await first.locator('p').first().boundingBox())!;
    const badgeBox = (await first.getByRole('link').boundingBox())!;
    const rowBox = (await first.boundingBox())!;
    expect(badgeBox.x).toBeGreaterThan(textBox.x + textBox.width - 1);
    expect(badgeBox.y + badgeBox.height).toBeLessThanOrEqual(rowBox.y + rowBox.height + 1);
    if (screenshotDir) await page.screenshot({ path: `${screenshotDir}/rows-1280.png`, fullPage: false });
    expect(consoleErrors).toEqual([]);
    await page.close();
  });

  test('390 px: the badge has its own right-aligned line under the row text; no horizontal scroll at 390 or 320; axe clean', async () => {
    const page = await customerA.newPage();
    await page.setViewportSize({ width: 390, height: 844 });
    await openEdited(page);
    const rows = page.locator('li');
    for (const [index, status] of statuses.entries()) {
      const li = rows.nth(index);
      const rowBox = (await li.boundingBox())!;
      const textBox = (await li.locator('div').first().boundingBox())!;
      const amountBox = (await li.locator('span.tabular-nums').boundingBox())!;
      const badgeBox = (await li.getByRole('link', { name: `Dispute status: ${labels[status]}`, exact: true }).boundingBox())!;
      // Under the merchant/number text, not beside it; right edge on the amount's right edge; inside the row.
      expect(badgeBox.y, `${status} below the text`).toBeGreaterThanOrEqual(textBox.y + textBox.height - 1);
      expect(Math.abs(badgeBox.x + badgeBox.width - (amountBox.x + amountBox.width)), `${status} right-aligned`).toBeLessThanOrEqual(2);
      expect(badgeBox.x, `${status} inside the row`).toBeGreaterThanOrEqual(rowBox.x);
      // The text block keeps the full left column, as wide as on a row without a badge.
      expect(textBox.width, `${status} text width`).toBeGreaterThan(150);
    }
    expect(await axeViolations(page), 'axe at 390').toEqual([]);
    await rows.nth(2).scrollIntoViewIfNeeded();
    if (screenshotDir) await page.screenshot({ path: `${screenshotDir}/rows-390.png` });
    for (const width of [390, 320]) {
      await page.setViewportSize({ width, height: 844 });
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), `${width} px`).toBeLessThanOrEqual(0);
    }
    expect(await axeViolations(page), 'axe at 320').toEqual([]);
    await page.close();
  });

  test('following an edited badge opens /disputes/<id>; the API has no dispute under that ID, so it shows the one not-found screen and no other dispute data', async () => {
    const page = await customerA.newPage();
    await openEdited(page);
    await page.locator('li').first().getByRole('link').click();
    await expect(page).toHaveURL(`${origin}/disputes/${disputeIds.submitted}`);
    await expect(page.getByRole('heading', { level: 1, name: "We can't find that dispute" })).toBeVisible();
    expect(await bodyText(page)).not.toMatch(/escalated/i);
    await page.close();
  });
});

if (screenshotDir) mkdirSync(screenshotDir, { recursive: true });
