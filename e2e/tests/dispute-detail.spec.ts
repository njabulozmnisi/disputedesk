import AxeBuilder from '@axe-core/playwright';
import { execFileSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { expectProblemShape, fixtureLogin, ownDisputable, queryDatabase, topicRecords } from './support';

// Story 3.3: a customer reads one dispute and its timeline. Network level (the detail screen is story 3.12, tested in the last block of this file), signed in
// through the hosted Zitadel login as the seeded customers. A and B are linked, C is not. Database facts are read through
// qa_readonly. A transaction is disputed once: the spec raises on its reserved transactions (support.ts).
// There is no outbox relay before story 4.1, so the events hop is the Pending outbox row and an empty topic.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const contexts: Record<string, BrowserContext> = {};
const unknownId = '01a189e2-0000-7000-8000-000000000000';

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

async function raiseOnReserved(api: APIRequestContext, accountRef: string, customer: 'a' | 'b') {
  const list = await api.get('/api/v1/transactions?limit=50');
  const row = ownDisputable((await list.json()).items as { transactionId: string; disputable: boolean }[], 'dispute-detail', customer)[0];
  expect(row, `a reserved ${accountRef} transaction is disputable (reservedTransactions in support.ts)`).toBeTruthy();
  return raise(api, { accountRef, transactionId: row!.transactionId, reason: 'unrecognised', note: 'qa 3.3' });
}

async function raise(api: APIRequestContext, body: object) {
  const token = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  return api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"qa33-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: body,
    maxRedirects: 0,
  });
}

const auditCount = () => Number(queryDatabase('select count(*) from "AuditLogEntries"'));
const noSessionValue = (headers: Record<string, string>) => {
  const { date: _date, 'set-cookie': cookie, traceparent: _t, ...rest } = headers;
  const attributes = cookie === undefined ? '' : cookie.replace(/^[^=]+=[^;]*/, '');
  return JSON.stringify({ rest: Object.entries(rest).sort(), attributes });
};
const withoutTraceId = ({ traceId: _ignored, ...rest }: Record<string, unknown>) => rest;

let own: { path: string; id: string; text: string };
let otherId: string;

test.beforeAll(async ({ browser }) => {
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');
  const raised = await raiseOnReserved(contexts.a.request, 'ACC-A-1', 'a');
  expect(raised.status()).toBe(201);
  const path = raised.headers().location;
  own = { path, id: path.split('/').pop()!, text: await raised.text() };
  const raisedByB = await raiseOnReserved(contexts.b.request, 'ACC-B-1', 'b');
  expect(raisedByB.status()).toBe(201);
  otherId = raisedByB.headers().location.split('/').pop()!;
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

test('the raise Location serves the detail: 200, byte-equal to the 201, no-store, outcome null, stored events behind it', async () => {
  expect(own.path).toBe(`/api/v1/disputes/${own.id}`);
  const response = await contexts.a.request.get(own.path);
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  expect(response.headers()['content-type']).toContain('application/json');
  expect(await response.text()).toBe(own.text);
  const body = JSON.parse(own.text);
  expect(body.outcome).toBeNull();
  expect(Object.keys(body).sort()).toEqual(
    ['createdAt', 'customerStatus', 'disputeId', 'disputedAmount', 'expectedAmount', 'note', 'outcome', 'reason', 'timeline', 'transaction'].sort());
  expect(body.timeline).toHaveLength(1);
  expect(Object.keys(body.timeline[0]).sort()).toEqual(['action', 'actor', 'customerStatus', 'note', 'occurredAt']);
  expect(body.timeline[0]).toMatchObject({ action: 'raise', customerStatus: 'submitted', actor: 'you' });

  const stored = queryDatabase(`select string_agg("Version" || ':' || "EventType" || ':' || "ActorType", ',' order by "Version") from "StoredEvents" where "DisputeId" = '${own.id}'`);
  expect(stored).toBe('1:DisputeRaised:Customer');
  expect(queryDatabase(`select "Status" || ':' || coalesce("Outcome"::text, 'null') from "Disputes" where "Id" = '${own.id}'`)).toBe('Submitted:null');
  expect(queryDatabase(`select "EventType" || ':' || "Status" from "OutboxMessages" where "DisputeId" = '${own.id}'`)).toMatch(/^disputedesk\.dispute\.raised\.v1:(Pending|Sent)$/); // the story 4.1 relay may already have sent it
  // The customer wire view holds no internal value.
  const wire = own.text;
  expect(wire).not.toContain('CUS-0000');
  expect(wire).not.toContain('worker:');
  expect(wire).not.toContain('DisputeRaised');
});

test("another customer's dispute and an unknown one get the same 404, echoing nothing", async () => {
  const other = await contexts.a.request.get(`/api/v1/disputes/${otherId}`);
  const unknown = await contexts.a.request.get(`/api/v1/disputes/${unknownId}`);
  expect(other.status()).toBe(404);
  expect(unknown.status()).toBe(404);
  expect(noSessionValue(other.headers())).toBe(noSessionValue(unknown.headers()));
  const otherBody = await other.json();
  expect(expectProblemShape(otherBody, 'not-found', 404)).toEqual([]);
  expect(withoutTraceId(otherBody)).toEqual(withoutTraceId(await unknown.json()));
  expect(JSON.stringify(otherBody)).not.toContain(otherId);
});

test("raising on another customer's transaction and on an unknown one get the same 404", async () => {
  const body = (transactionId: string) => ({ accountRef: 'ACC-B-1', transactionId, reason: 'unrecognised' });
  const other = await raise(contexts.a.request, body('TXN-B-0001'));
  const unknown = await raise(contexts.a.request, body('TXN-ZZZ-9999'));
  expect(other.status()).toBe(404);
  expect(unknown.status()).toBe(404);
  expect(noSessionValue(other.headers())).toBe(noSessionValue(unknown.headers()));
  expect(withoutTraceId(await other.json())).toEqual(withoutTraceId(await unknown.json()));
});

test('authorization states: 401, then 403, then 400; bearer alone is 401; no read writes an audit row', async ({ playwright }) => {
  const before = auditCount();
  const anonymous = await playwright.request.newContext({ baseURL: origin });
  const anonymousResponse = await anonymous.get(`/api/v1/disputes/${unknownId}`, { maxRedirects: 0 });
  expect(anonymousResponse.status()).toBe(401);
  expect(anonymousResponse.headers()['www-authenticate']).toBeUndefined();
  expect(expectProblemShape(await anonymousResponse.json(), 'unauthorized', 401)).toEqual([]);
  expect((await anonymous.get('/api/v1/disputes/not-a-guid', { maxRedirects: 0 })).status()).toBe(401);
  const bearer = await anonymous.get(`/api/v1/disputes/${unknownId}`, { headers: { Authorization: 'Bearer a.b.c' }, maxRedirects: 0 });
  expect(bearer.status()).toBe(401);
  await anonymous.dispose();

  const unlinked = await contexts.c.request.get(`/api/v1/disputes/${unknownId}`);
  expect(unlinked.status()).toBe(403);
  expect((await unlinked.json()).type).toMatch(/linking-required\.md$/);
  expect((await contexts.c.request.get('/api/v1/disputes/not-a-guid')).status()).toBe(403);

  const malformed = await contexts.a.request.get('/api/v1/disputes/not-a-guid');
  expect(malformed.status()).toBe(400);
  const malformedBody = await malformed.json();
  expect(expectProblemShape(malformedBody, 'validation', 400)).toEqual([]);
  expect(JSON.stringify(malformedBody)).not.toContain('not-a-guid');

  expect(auditCount()).toBe(before);
});

test('the detail answers 200 from the stored snapshot while core banking is down', async () => {
  execFileSync('docker', ['stop', 'disputedesk-core-banking-1']);
  try {
    const response = await contexts.a.request.get(own.path);
    expect(response.status()).toBe(200);
    expect(await response.text()).toBe(own.text);
  } finally {
    execFileSync('docker', ['start', 'disputedesk-core-banking-1']);
    await expect.poll(() => execFileSync('docker', ['inspect', '-f', '{{.State.Health.Status}}', 'disputedesk-core-banking-1'], { encoding: 'utf8' }).trim(),
      { timeout: 60_000 }).toBe('healthy');
  }
});

// ---------------------------------------------------------------------------------------------------------------------
// Story 3.12: the dispute detail, timeline and withdraw screen in the browser, through the real gateway, BFF, API,
// database and topic. Customer B's two reserved transactions (TXN-B-0002 and TXN-B-0012, support.ts) are raised in
// beforeAll: `withdrawn` is withdrawn for real, `open` stays open and takes every mocked refusal. This stack has no
// operations actions before story 5.2 merges into the tested tree, so a Resolved, Rejected or Upheld dispute (and an
// out-of-contract value) is shown by editing the detail response in the browser, as story 3.10's spec does for the list.
// ---------------------------------------------------------------------------------------------------------------------

const screenshotDir = process.env.QA_SCREENSHOT_DIR;
const knownRadixStyleViolation = /^Applying inline style violates the following Content Security Policy directive 'style-src 'self''\./;
// The browser logs every 4xx and 5xx response; the spec provokes those on purpose and asserts them as responses.
const refusedOnPurpose = /^Failed to load resource: the server responded with a status of (404|409|500)/;
const notFoundTitle = "We can't find that dispute";
const problemBase = 'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/';
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
if (screenshotDir) mkdirSync(screenshotDir, { recursive: true });

type DetailBody = {
  disputeId: string; customerStatus: string; outcome: string | null; reason: string; expectedAmount: unknown;
  disputedAmount: { amount: string; currency: string };
  transaction: { description: string; maskedCard: string | null; accountLast4: string | null; transactionDate: string };
  timeline: { action: string; customerStatus: string; actor: string; occurredAt: string; note: string | null }[];
};

const detailUrl = (id: string) => new RegExp(`/api/v1/disputes/${id}$`, 'i');
const withdrawUrl = (id: string) => new RegExp(`/api/v1/disputes/${id}/withdraw$`, 'i');
const problem = (slug: string, status: number, extra: object = {}) => ({
  contentType: 'application/problem+json',
  status,
  body: JSON.stringify({ type: `${problemBase}${slug}.md`, title: 'Raw title must not show', detail: 'Raw detail must not show', status, traceId: 'qa312-trace-0123456789abcdef', ...extra }),
});
// The unknown route renders outside the signed-in shell (no main navigation); the screen itself is the main region.
const mainText = async (page: Page) => (await page.locator('main').innerText()).replace(/\s+/g, ' ');
const text = async (page: Page) => (await page.locator('body').innerText()).replace(/\s+/g, ' ');

async function axeFindings(page: Page, label: string) {
  const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  return result.violations.map((violation) => `${label} ${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join('; ')}`);
}

function watchConsole(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && !knownRadixStyleViolation.test(message.text()) && !refusedOnPurpose.test(message.text())) errors.push(message.text());
  });
  return errors;
}

async function shot(page: Page, name: string) {
  if (screenshotDir) await page.screenshot({ path: `${screenshotDir}/${name}.png`, fullPage: true });
}

async function activeElementSummary(page: Page) {
  return page.evaluate(() => {
    const element = document.activeElement;
    return { tag: element?.tagName ?? '', text: (element?.textContent ?? '').trim().slice(0, 40), inDialog: element?.closest('[role="alertdialog"]') !== null };
  });
}

/** Shows an edited detail: the real response with the given changes. */
async function editDetail(page: Page, id: string, change: (body: DetailBody) => void) {
  await page.route(detailUrl(id), async (route) => {
    const response = await route.fetch();
    const body = (await response.json()) as DetailBody;
    change(body);
    await route.fulfill({ response, json: body });
  });
}

test.describe('story 3.12: the detail screen', () => {
  test.describe.configure({ mode: 'serial' });
  let withdrawnId: string;
  let openId: string;
  let openBody: DetailBody;
  let openDescription: string;
  let thirdContext: BrowserContext;

  /** Raises on the first of the candidate transactions that is still disputable; an earlier run's open dispute is reused when asked. */
  async function raiseB(candidates: string[], reuseOpen = false) {
    const list = await contexts.b.request.get('/api/v1/transactions?limit=50');
    const rows = (await list.json()).items as { transactionId: string; accountRef: string; disputable: boolean; disputeId?: string | null; customerStatus?: string | null }[];
    const earlier = reuseOpen ? rows.find((item) => candidates.includes(item.transactionId) && item.customerStatus === 'submitted') : undefined;
    if (earlier?.disputeId) return earlier.disputeId;
    const row = candidates.flatMap((id) => rows.filter((item) => item.transactionId === id && item.disputable))[0];
    expect(row, `one of ${candidates.join(', ')} is still disputable (reserved for dispute-detail in support.ts; recreate the volumes if not)`).toBeTruthy();
    const token = (await (await contexts.b.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    const created = await contexts.b.request.post('/api/v1/disputes', {
      headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"qa312-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
      data: { accountRef: row.accountRef, transactionId: row.transactionId, reason: 'unrecognised', note: 'qa 3.12', expectedAmount: null },
    });
    expect(created.status()).toBe(201);
    return ((await created.json()) as DetailBody).disputeId;
  }

  test.beforeAll(async ({ browser }) => {
    withdrawnId = await raiseB(['TXN-B-0002', 'TXN-B-0021', 'TXN-B-0023'], true);
    openId = await raiseB(['TXN-B-0012'], true);
    openBody = (await (await contexts.b.request.get(`/api/v1/disputes/${openId}`)).json()) as DetailBody;
    openDescription = openBody.transaction.description;
    thirdContext = await signIn(browser, 'customer-b');
  });
  test.afterAll(async () => thirdContext?.close());

  test('criterion 1: only a GUID reaches the API; every not-found is the same screen; upper case asks for lower case', async () => {
    const page = await contexts.b.newPage();
    const consoleErrors = watchConsole(page);
    const requests: string[] = [];
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/v1/disputes')) requests.push(`${request.method()} ${path}`);
    });
    const bodies: Record<string, string> = {};
    const malformed = ['/disputes/abc', '/disputes/..%2Fx%3Fy%23z', `/disputes/${openId}%2F..`, `/disputes/%7B${openId}%7D`, `/disputes/{${openId}}`, `/disputes/${openId}x`, '/disputes/not-a-route/extra', '/nothing-here'];
    for (const path of malformed) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(notFoundTitle);
      await expect(page.getByRole('link', { name: 'Go to your disputes' })).toBeVisible();
      expect(await page.getByRole('heading', { level: 1 }).count(), path).toBe(1);
      bodies[path] = await mainText(page);
    }
    expect(requests, 'malformed IDs send no request under /api/v1/disputes').toEqual([]);

    // A GUID the API answers 404: an unknown one, and another customer's dispute (customer A asks for B's).
    const unknown = '0197c3a4-0000-7000-8000-000000000999';
    await page.goto(`/disputes/${unknown}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(notFoundTitle);
    bodies.unknown = await mainText(page);
    expect(requests).toEqual([`GET /api/v1/disputes/${unknown}`]);
    await shot(page, 'not-found-1280');
    expect(await axeFindings(page, 'not-found')).toEqual([]);
    await page.close();

    const pageA = await contexts.a.newPage();
    const responseA = pageA.waitForResponse((response) => new URL(response.url()).pathname === `/api/v1/disputes/${openId}`);
    await pageA.goto(`/disputes/${openId}`);
    const refused = await responseA;
    expect(refused.status()).toBe(404);
    expect(expectProblemShape(await refused.json(), 'not-found', 404)).toEqual([]);
    await expect(pageA.getByRole('heading', { level: 1 })).toHaveText(notFoundTitle);
    bodies.other = await mainText(pageA);
    expect(bodies.other).not.toContain(openDescription);
    await pageA.close();
    for (const key of Object.keys(bodies)) expect(bodies[key], key).toBe(bodies['/disputes/abc']);
    expect(consoleErrors).toEqual([]);

    // Upper case in the URL: the router matches the lower-cased form and the request carries the lower-case GUID.
    const upper = await contexts.b.newPage();
    const asked: string[] = [];
    upper.on('request', (request) => {
      if (new URL(request.url()).pathname.startsWith('/api/v1/disputes/')) asked.push(new URL(request.url()).pathname);
    });
    await upper.goto(`/disputes/${openId.toUpperCase()}`);
    await expect(upper.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    expect(asked).toEqual([`/api/v1/disputes/${openId}`]);
    await upper.close();
  });

  test('criterion 2: loading, loaded, failure with Try again; one h1; a direct load leaves focus alone; the data is exactly the API response', async () => {
    const page = await contexts.b.newPage();
    const consoleErrors = watchConsole(page);
    await page.setViewportSize({ width: 1280, height: 900 });
    // Loading: held for 2 s.
    await page.route(detailUrl(openId), async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 2000));
      await route.continue();
    });
    await page.goto(`/disputes/${openId}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Dispute');
    expect(await page.title()).toBe('Dispute · DisputeDesk');
    await expect(page.locator('[aria-busy="true"]')).toHaveCount(1);
    expect(await page.locator('ol li').count()).toBe(3);
    expect(await axeFindings(page, 'loading')).toEqual([]);
    await shot(page, 'loading-1280');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    await page.unrouteAll({ behavior: 'wait' });

    // Direct load: focus stays on the body; the first Tab reaches the skip link; one h1.
    await page.goto(`/disputes/${openId}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    await expect(page.locator('[aria-busy="true"]')).toHaveCount(0);
    expect(await page.getByRole('heading', { level: 1 }).count()).toBe(1);
    expect(await page.title()).toBe(`${openDescription} · DisputeDesk`);
    expect((await activeElementSummary(page)).tag).toBe('BODY');
    await page.keyboard.press('Tab');
    expect((await activeElementSummary(page)).text).toBe('Skip to main content');

    // Loaded content equals the API response (hop: network), with nothing shown that the response does not hold.
    const content = await text(page);
    expect(content).toContain('Submitted');
    expect(content.replace(/\u202F/g, '')).toContain(`R${openBody.disputedAmount.amount}`);
    expect(content).toContain('Unrecognised');
    expect(content).toContain(openBody.transaction.maskedCard!.slice(0, 6) + '•• •••• ' + openBody.transaction.maskedCard!.slice(-4));
    expect(content).not.toMatch(/Transaction amount|Expected amount|Dispute reference|If you disagree with this outcome/);
    expect(content).toContain('Back to disputes');
    expect(content).not.toContain(openId);
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
    expect(await axeFindings(page, 'loaded')).toEqual([]);
    await shot(page, 'open-1280');
    const columns = await page.evaluate(() => {
      const heading = document.querySelector('h1')!.getBoundingClientRect();
      const timeline = document.querySelector('ol')!.getBoundingClientRect();
      return { headingLeft: heading.left, timelineLeft: timeline.left, headingTop: heading.top, timelineTop: timeline.top };
    });
    expect(columns.timelineLeft, 'summary and timeline sit in two columns at 1280').toBeGreaterThan(columns.headingLeft + 300);

    // Timeline: one raise entry, the time in South African time, and the open-dispute promise as a paragraph, not a list item.
    const entry = openBody.timeline[0];
    const items = page.locator('ol li');
    await expect(items).toHaveCount(1);
    await expect(items.first()).toContainText('Submitted');
    await expect(items.first()).toContainText('You submitted this dispute.');
    const expectedTime = new Date(new Date(entry.occurredAt).getTime() + 2 * 3600_000);
    const month = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][expectedTime.getUTCMonth()];
    const pad = (value: number) => String(value).padStart(2, '0');
    const expectedText = `${expectedTime.getUTCDate()} ${month} ${expectedTime.getUTCFullYear()}, ${pad(expectedTime.getUTCHours())}:${pad(expectedTime.getUTCMinutes())} SAST`;
    await expect(items.first().locator('time')).toHaveText(expectedText);
    await expect(items.first().locator('time')).toHaveAttribute('datetime', entry.occurredAt);
    await expect(items.first()).toContainText(`You · ${expectedText}`);
    await expect(page.getByText("We'll update the timeline as your dispute moves.")).toHaveCount(1);
    await expect(items.filter({ hasText: "We'll update" })).toHaveCount(0);

    // Failure: a 500 shows the notice and the reference only; Try again recovers and moves focus to the h1.
    let fail = true;
    await page.route(detailUrl(openId), async (route) => {
      if (fail) await route.fulfill(problem('internal', 500));
      else await route.continue();
    });
    await page.goto(`/disputes/${openId}`);
    await expect(page.getByText('Something went wrong on our side. Try again.')).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Dispute');
    const failure = await text(page);
    expect(failure).toContain('qa312-trace-0123456789abcdef');
    expect(failure).not.toMatch(/Raw title|Raw detail/);
    expect(failure).not.toContain(openDescription);
    expect(await axeFindings(page, 'failure')).toEqual([]);
    await shot(page, 'failure-1280');
    fail = false;
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    expect((await activeElementSummary(page)).text).toBe(openDescription.slice(0, 40));
    expect(consoleErrors).toEqual([]);

    // 320 px: no horizontal scroll, axe clean.
    await page.setViewportSize({ width: 320, height: 700 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(0);
    expect(await axeFindings(page, 'loaded-320')).toEqual([]);
    await shot(page, 'open-320');
    await page.close();
  });

  test('criterion 3: every timeline line, the actor and the time in SAST; unknown values render nothing; the Rejected guidance', async () => {
    const page = await contexts.b.newPage();
    await page.setViewportSize({ width: 1280, height: 900 });
    await editDetail(page, openId, (body) => {
      body.customerStatus = 'resolved';
      body.outcome = 'rejected';
      body.timeline = [
        { action: 'raise', customerStatus: 'submitted', actor: 'you', occurredAt: '2026-10-26T12:32:00Z', note: 'a <b>bold</b> https://example.test/x note' },
        { action: 'startReview', customerStatus: 'underReview', actor: 'theBank', occurredAt: '2026-09-03T08:05:00Z', note: null },
        { action: 'escalate', customerStatus: 'underReviewPrioritised', actor: 'theBank', occurredAt: '2026-09-04T22:30:00Z', note: null },
        { action: 'resolve', customerStatus: 'resolved', actor: 'theBank', occurredAt: '2026-10-27T10:00:00Z', note: null },
        { action: 'archive', customerStatus: 'escalated', actor: 'robot', occurredAt: '2026-10-28T10:00:00Z', note: null },
      ];
    });
    await page.goto(`/disputes/${openId}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    const items = page.locator('ol li');
    await expect(items).toHaveCount(5);
    await expect(items.nth(0).locator('time')).toHaveText('26 Oct 2026, 14:32 SAST');
    await expect(items.nth(1).locator('time')).toHaveText('3 Sep 2026, 10:05 SAST');
    await expect(items.nth(2).locator('time')).toHaveText('5 Sep 2026, 00:30 SAST');
    await expect(items.nth(0)).toContainText('“a <b>bold</b> https://example.test/x note”');
    expect(await items.nth(0).locator('a, b').count(), 'no autolinking, no markup').toBe(0);
    await expect(items.nth(1)).toContainText('The bank started reviewing your dispute.');
    await expect(items.nth(2)).toContainText('The bank prioritised the review of your dispute.');
    await expect(items.nth(3)).toContainText("The bank didn't uphold your dispute.");
    await expect(items.nth(3)).toContainText('Rejected');
    const unknownEntry = (await items.nth(4).innerText()).replace(/\s+/g, ' ');
    expect(unknownEntry).not.toMatch(/archive|escalated|Escalated|robot/);
    expect(await text(page)).toContain("If you disagree with this outcome, you can complain to the bank first. If you're still not satisfied after the bank has answered your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that is free for customers.");
    await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
    await expect(page.getByText("We'll update the timeline")).toHaveCount(0);
    expect(await axeFindings(page, 'rejected')).toEqual([]);
    await shot(page, 'rejected-1280');
    await page.setViewportSize({ width: 320, height: 700 });
    await shot(page, 'rejected-320');
    expect(await axeFindings(page, 'rejected-320')).toEqual([]);

    // Upheld, with the out-of-contract reason and status and an expected amount.
    await page.unrouteAll({ behavior: 'wait' });
    await editDetail(page, openId, (body) => {
      body.customerStatus = 'resolved';
      body.outcome = 'upheld';
      body.reason = 'something-new';
      body.expectedAmount = { amount: '10.00', currency: 'ZAR' };
      body.timeline = [body.timeline[0], { action: 'resolve', customerStatus: 'resolved', actor: 'theBank', occurredAt: '2026-10-27T10:00:00Z', note: null }];
    });
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`/disputes/${openId}`);
    await expect(page.locator('ol li')).toHaveCount(2);
    const upheld = await text(page);
    expect(upheld).toContain('The bank agreed with you.');
    expect(upheld).toContain('Upheld');
    expect(upheld).toContain('Transaction amount');
    expect(upheld).toContain('Expected amount');
    expect(upheld).not.toContain('something-new');
    expect(upheld).not.toMatch(/Reason /);
    expect(upheld).not.toContain('If you disagree with this outcome');
    await shot(page, 'upheld-1280');
    expect(await axeFindings(page, 'upheld')).toEqual([]);
    await page.close();
  });

  test('criteria 4 and 5: the dialog traps focus, Escape and Keep close it, the note bounds and the pending state', async () => {
    const page = await contexts.b.newPage();
    const consoleMessages: string[] = [];
    page.on('console', (message) => {
      if (message.type() === 'error') consoleMessages.push(message.text());
    });
    await page.setViewportSize({ width: 1280, height: 900 });
    const posts: { url: string; body: string; token: string | undefined; key: string | undefined }[] = [];
    let respond: 'slow' | 'fail' = 'fail';
    await page.route(withdrawUrl(openId), async (route) => {
      const request = route.request();
      posts.push({ url: request.url(), body: request.postData() ?? '', token: request.headers()['x-xsrf-token'], key: request.headers()['idempotency-key'] });
      if (respond === 'slow') await new Promise((resolve) => setTimeout(resolve, 2000));
      await route.fulfill(problem('internal', 500));
    });
    await page.goto(`/disputes/${openId}`);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    const withdrawButton = page.getByRole('button', { name: 'Withdraw dispute' });
    await withdrawButton.click();
    const dialog = page.getByRole('alertdialog', { name: 'Withdraw this dispute?' });
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText("You can't dispute this transaction again after you withdraw.");
    expect((await activeElementSummary(page)).text, 'initial focus on the least destructive action').toBe('Keep dispute');
    await shot(page, 'dialog-1280');
    expect(await axeFindings(page, 'dialog')).toEqual([]);
    for (let press = 0; press < 6; press += 1) {
      await page.keyboard.press('Tab');
      expect((await activeElementSummary(page)).inDialog, `Tab ${press + 1} stays inside`).toBe(true);
    }
    await page.keyboard.press('Shift+Tab');
    expect((await activeElementSummary(page)).inDialog).toBe(true);
    await page.keyboard.press('Escape');
    await expect(dialog).toHaveCount(0);
    await expect(withdrawButton).toBeFocused();
    await withdrawButton.click();
    await page.getByRole('button', { name: 'Keep dispute' }).click();
    await expect(dialog).toHaveCount(0);
    await expect(withdrawButton).toBeFocused();

    // Note bounds. 501 after trimming: refused with no request, the summary takes focus, the field is not cut.
    await withdrawButton.click();
    const note = page.getByLabel('Note (optional)');
    await note.fill('x'.repeat(501));
    await expect(dialog.getByText('501 / 500')).toBeVisible();
    await dialog.getByRole('button', { name: 'Withdraw dispute' }).click();
    await expect(dialog.getByText('Your note must be 500 characters or fewer.')).toHaveCount(2);
    expect((await activeElementSummary(page)).text).toContain('Check the details below');
    expect(await note.inputValue()).toHaveLength(501);
    expect(posts).toHaveLength(0);
    await shot(page, 'dialog-error-1280');
    expect(await axeFindings(page, 'dialog-error')).toEqual([]);
    // 500 'a' padded with spaces: accepted, sent trimmed.
    await note.fill(`   ${'a'.repeat(500)}   `);
    await expect(dialog.getByText('500 / 500')).toBeVisible();
    await dialog.getByRole('button', { name: 'Withdraw dispute' }).click();
    await expect(dialog.getByText('Something went wrong on our side. Try again.')).toBeVisible();
    expect(posts).toHaveLength(1);
    expect(JSON.parse(posts[0].body)).toEqual({ note: 'a'.repeat(500) });
    expect(posts[0].url.endsWith(`/api/v1/disputes/${openId}/withdraw`)).toBe(true);
    expect(new URL(posts[0].url).search).toBe('');
    expect(posts[0].token, 'X-XSRF-TOKEN is sent').toBeTruthy();
    expect(posts[0].key, 'no Idempotency-Key').toBeUndefined();
    expect(await note.inputValue()).toBe(`   ${'a'.repeat(500)}   `);
    // Whitespace only: no note, the body is {}.
    await note.fill('     ');
    await expect(dialog.getByText('0 / 500')).toBeVisible();
    await dialog.getByRole('button', { name: 'Withdraw dispute' }).click();
    await expect.poll(() => posts.length).toBe(2);
    expect(JSON.parse(posts[1].body)).toEqual({});
    // Pending: a delayed response. Escape and Keep do nothing, a second press sends nothing.
    respond = 'slow';
    const confirm = dialog.getByRole('button', { name: /^Withdraw/ });
    await note.fill(' pending ');
    await confirm.click();
    await expect(dialog.getByRole('button', { name: 'Withdrawing…' })).toHaveAttribute('aria-disabled', 'true');
    // force: Playwright treats aria-disabled as not actionable, and the point is that a press does nothing.
    await dialog.getByRole('button', { name: 'Withdrawing…' }).click({ force: true });
    await page.keyboard.press('Escape');
    await dialog.getByRole('button', { name: 'Keep dispute' }).click({ force: true });
    await expect(dialog.getByRole('button', { name: 'Keep dispute' })).toHaveAttribute('aria-disabled', 'true');
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Withdraw dispute' })).toBeVisible({ timeout: 10_000 });
    expect(posts).toHaveLength(3);
    expect(JSON.parse(posts[2].body)).toEqual({ note: 'pending' });
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);
    expect(consoleMessages.filter((message) => !knownRadixStyleViolation.test(message) && !refusedOnPurpose.test(message))).toEqual([]);
    await page.close();
  });

  test('criterion 6, refusals: each response shows its words; the dialog stays open unless stated; a failing refetch leaves no stale card', async () => {
    const cases: { name: string; respond: (route: import('@playwright/test').Route) => Promise<void>; check: (page: Page) => Promise<void> }[] = [
      {
        name: '500',
        respond: (route) => route.fulfill(problem('internal', 500)),
        check: async (page) => {
          await expect(page.getByRole('alertdialog')).toContainText('Something went wrong on our side. Try again.');
          await expect(page.getByRole('alertdialog')).toContainText('qa312-trace-0123456789abcdef');
          expect(await text(page)).not.toMatch(/Raw title|Raw detail/);
          await expect(page.getByLabel('Note (optional)')).toHaveValue('keep me');
        },
      },
      {
        name: 'network failure',
        respond: (route) => route.abort('failed'),
        check: async (page) => {
          await expect(page.getByRole('alertdialog')).toContainText('Something went wrong on our side. Try again.');
          await expect(page.getByLabel('Note (optional)')).toHaveValue('keep me');
        },
      },
      {
        name: '400 naming note',
        respond: (route) => route.fulfill(problem('validation', 400, { errors: { note: ['too long'] } })),
        check: async (page) => {
          await expect(page.getByRole('alertdialog').getByText('Your note must be 500 characters or fewer.')).toHaveCount(2);
          expect((await activeElementSummary(page)).text).toContain('Check the details below');
        },
      },
      {
        name: '400 not naming note',
        respond: (route) => route.fulfill(problem('validation', 400, { errors: { other: ['x'] } })),
        check: async (page) => {
          await expect(page.getByRole('alertdialog')).toContainText('Something went wrong on our side. Try again.');
          await expect(page.getByRole('alertdialog').getByText('Your note must be 500 characters or fewer.')).toHaveCount(0);
        },
      },
      {
        name: '404',
        respond: (route) => route.fulfill(problem('not-found', 404)),
        check: async (page) => {
          await expect(page.getByRole('alertdialog')).toHaveCount(0);
          await expect(page.getByRole('heading', { level: 1 })).toHaveText(notFoundTitle);
          await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
        },
      },
      {
        name: '409 with an unknown customerStatus',
        respond: (route) => route.fulfill(problem('action-not-allowed', 409, { customerStatus: 'escalated' })),
        check: async (page) => {
          await expect(page.getByRole('alertdialog')).toContainText("This dispute can't be withdrawn any more.");
        },
      },
      {
        name: '409 with an open customerStatus (version clash)',
        respond: (route) => route.fulfill(problem('action-not-allowed', 409, { customerStatus: 'underReview' })),
        check: async (page) => {
          await expect(page.getByRole('alertdialog')).toContainText('Something went wrong on our side. Try again.');
          await expect(page.getByRole('alertdialog')).toContainText('qa312-trace-0123456789abcdef');
          await expect(page.getByLabel('Note (optional)')).toHaveValue('keep me');
        },
      },
    ];
    for (const refusal of cases) {
      const page = await contexts.b.newPage();
      let detailReads = 0;
      await page.route(detailUrl(openId), async (route) => {
        detailReads += 1;
        await route.continue();
      });
      await page.route(withdrawUrl(openId), refusal.respond);
      await page.goto(`/disputes/${openId}`);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
      await page.getByRole('button', { name: 'Withdraw dispute' }).click();
      await page.getByLabel('Note (optional)').fill('keep me');
      await page.getByRole('alertdialog').getByRole('button', { name: 'Withdraw dispute' }).click();
      await refusal.check(page);
      if (refusal.name.startsWith('409 with an open')) await expect.poll(() => detailReads, { message: 'the detail is invalidated' }).toBe(2);
      await page.close();
    }

    // 409 resolved: the notice stays readable after the refetch removes the Withdraw button.
    const resolved = await contexts.b.newPage();
    await resolved.route(withdrawUrl(openId), (route) => route.fulfill(problem('action-not-allowed', 409, { customerStatus: 'resolved' })));
    await resolved.goto(`/disputes/${openId}`);
    await expect(resolved.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    await editDetail(resolved, openId, (body) => {
      body.customerStatus = 'resolved';
      body.outcome = 'upheld';
      body.timeline.push({ action: 'resolve', customerStatus: 'resolved', actor: 'theBank', occurredAt: '2026-10-29T10:00:00Z', note: null });
    });
    await resolved.getByRole('button', { name: 'Withdraw dispute' }).click();
    await resolved.getByRole('alertdialog').getByRole('button', { name: 'Withdraw dispute' }).click();
    await expect(resolved.getByRole('alertdialog')).toContainText("This dispute is now Resolved. It can't be withdrawn.");
    await expect(resolved.locator('main, body').first().getByText('Upheld').first()).toBeVisible();
    await expect(resolved.getByRole('button', { name: 'Withdraw dispute', exact: true })).toHaveCount(1); // the dialog's own
    await shot(resolved, 'dialog-resolved-1280');
    await resolved.getByRole('button', { name: 'Keep dispute' }).click();
    await expect(resolved.getByRole('alertdialog')).toHaveCount(0);
    await expect(resolved.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
    await expect(resolved.getByRole('heading', { level: 1 }), 'focus goes to the h1 when the button is gone').toBeFocused();
    await resolved.close();

    // 409 resolved, then the refetch fails: the failure shows, no stale card and no dialog.
    const stale = await contexts.b.newPage();
    await stale.route(withdrawUrl(openId), (route) => route.fulfill(problem('action-not-allowed', 409, { customerStatus: 'resolved' })));
    await stale.goto(`/disputes/${openId}`);
    await expect(stale.getByRole('heading', { level: 1 })).toHaveText(openDescription);
    await stale.getByRole('button', { name: 'Withdraw dispute' }).click();
    await stale.route(detailUrl(openId), (route) => route.fulfill(problem('internal', 500)));
    await stale.getByRole('alertdialog').getByRole('button', { name: 'Withdraw dispute' }).click();
    await expect(stale.getByText('Something went wrong on our side. Try again.').first()).toBeVisible();
    await expect(stale.getByRole('alertdialog')).toHaveCount(0);
    expect(await text(stale)).not.toContain('Disputed amount');
    expect(await text(stale)).not.toContain(openDescription);
    await expect(stale.getByRole('button', { name: 'Try again' })).toBeVisible();
    await stale.close();
  });

  test('criteria 5 and 6, 200: the real withdraw; the stored event and outbox row; the topic record; caches; a stale tab gets 409 withdrawn', async () => {
    const page = await contexts.b.newPage();
    const consoleErrors = watchConsole(page);
    await page.setViewportSize({ width: 1280, height: 900 });
    const stalePage = await contexts.b.newPage();
    await stalePage.goto(`/disputes/${withdrawnId}`);
    await expect(stalePage.getByRole('button', { name: 'Withdraw dispute' })).toBeVisible();

    // Route change 1: a badge on the transactions list (page 1, any dispute of this customer) opens the detail and the h1 takes focus.
    await page.goto('/transactions');
    const firstBadge = page.getByRole('link', { name: /^Dispute status: / }).first();
    await expect(firstBadge).toBeVisible();
    const badgeHref = await firstBadge.getAttribute('href');
    await firstBadge.click();
    await expect(page).toHaveURL(`${origin}${badgeHref}`);
    await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
    await expect(page.getByRole('heading', { name: notFoundTitle })).toHaveCount(0);
    // Route change 2: the shell's Disputes link, then the dispute's own row.
    await page.getByRole('link', { name: 'Disputes', exact: true }).first().click();
    await page.locator(`a[href="/disputes/${withdrawnId}"]`).first().click();
    await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
    await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toBeVisible();

    const requests: string[] = [];
    page.on('request', (request) => {
      const parsed = new URL(request.url());
      if (request.resourceType() === 'fetch' || request.resourceType() === 'xhr') requests.push(`${request.method()} ${parsed.pathname}`);
    });
    // After the POST the detail GET would answer 500; the 200 body is written to the cache, so nothing reads it.
    let posted = false;
    await page.route(detailUrl(withdrawnId), (route) => (posted ? route.fulfill(problem('internal', 500)) : route.continue()));
    const eventsBefore = queryDatabase(`select count(*) from "StoredEvents" where "DisputeId" = '${withdrawnId}'`);
    expect(eventsBefore).toBe('1');
    await page.getByRole('button', { name: 'Withdraw dispute' }).click();
    await page.getByLabel('Note (optional)').fill('  changed my mind  ');
    const withdrawResponse = page.waitForResponse((response) => withdrawUrl(withdrawnId).test(response.url()));
    posted = true;
    await page.getByRole('alertdialog').getByRole('button', { name: 'Withdraw dispute' }).click();
    const response = await withdrawResponse;
    expect(response.status()).toBe(200);
    expect(JSON.parse(response.request().postData()!)).toEqual({ note: 'changed my mind' });
    expect(Object.keys(await response.json()).sort()).toEqual(Object.keys(openBody).sort());
    await expect(page.getByRole('alertdialog')).toHaveCount(0);
    await expect(page.locator('[role="status"][aria-live="polite"]')).toHaveText('Dispute withdrawn');
    await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
    await expect(page.getByText('Withdrawn').first()).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toBeFocused();
    const items = page.locator('ol li');
    await expect(items).toHaveCount(2);
    await expect(items.nth(1)).toContainText('You withdrew this dispute.');
    await expect(items.nth(1)).toContainText('“changed my mind”');
    await expect(page.getByText("We'll update the timeline")).toHaveCount(0);
    await page.waitForTimeout(400); // the dialog's fade-out
    await shot(page, 'withdrawn-1280');
    expect(await axeFindings(page, 'withdrawn')).toEqual([]);
    expect(requests.filter((request) => request.includes('/disputes/') && request.startsWith('GET')), 'no detail refetch after a 200').toEqual([]);
    expect(await page.evaluate(() => [localStorage.length, sessionStorage.length])).toEqual([0, 0]);

    // Caches: back through the SPA, both lists show the dispute as Withdrawn without a reload.
    await page.unrouteAll({ behavior: 'wait' });
    await page.getByRole('link', { name: 'Back to disputes' }).click();
    await expect(page).toHaveURL(`${origin}/disputes`);
    await expect(page.locator(`a[href="/disputes/${withdrawnId}"]`).first()).toContainText('Withdrawn');
    await page.getByRole('link', { name: 'Transactions', exact: true }).first().click();
    await expect(page).toHaveURL(`${origin}/transactions`);
    const apiRows = ((await (await contexts.b.request.get('/api/v1/transactions?limit=50')).json()).items as { disputeId?: string; customerStatus?: string }[]);
    expect(apiRows.find((item) => item.disputeId === withdrawnId)?.customerStatus).toBe('withdrawn');
    expect(requests.filter((request) => request === 'GET /api/v1/disputes').length, 'the disputes list was refetched').toBeGreaterThanOrEqual(1);
    expect(requests.filter((request) => request === 'GET /api/v1/transactions').length, 'the transactions list was refetched').toBeGreaterThanOrEqual(1);

    // Database (read-only role): status, two stored events, two outbox rows with the same IDs, no audit row for this dispute.
    expect(queryDatabase(`select "Status", "Version" from "Disputes" where "Id" = '${withdrawnId}'`)).toBe('Withdrawn|2');
    expect(queryDatabase(`select "EventType" from "StoredEvents" where "DisputeId" = '${withdrawnId}' order by "Version"`).split('\n')).toEqual(['DisputeRaised', 'DisputeWithdrawn']);
    expect(queryDatabase(`select count(*) from "StoredEvents" e join "OutboxMessages" o on o."Id" = e."Id" where e."DisputeId" = '${withdrawnId}'`)).toBe('2');
    expect(queryDatabase(`select count(*) from "AuditLogEntries" where "DisputeId" = '${withdrawnId}'`)).toBe('0');
    // Events: the relay publishes both with the dispute ID as the key and trace context in the headers.
    await expect.poll(() => topicRecords().filter((record) => record.key === withdrawnId).length, { timeout: 60_000 }).toBe(2);
    const records = topicRecords().filter((record) => record.key === withdrawnId);
    expect(records.every((record) => record.headers.some((header) => header.key === 'traceparent'))).toBe(true);
    const envelopes = records.map((record) => JSON.parse(record.value) as { type: string; data: { toStatus?: string } });
    expect(envelopes.map((envelope) => envelope.type).sort()).toEqual(['disputedesk.dispute.raised.v1', 'disputedesk.dispute.status-changed.v1']);
    expect(envelopes.find((envelope) => envelope.type.includes('status-changed'))!.data.toStatus).toBe('withdrawn');
    expect(records.map((record) => record.value).join(' '), 'the note is not in the event').not.toContain('changed my mind');

    // A stale tab: its Withdraw is refused 409 action-not-allowed with customerStatus withdrawn; the dialog closes on Withdrawn.
    await stalePage.getByRole('button', { name: 'Withdraw dispute' }).click();
    await stalePage.getByLabel('Note (optional)').fill('second try');
    const staleResponse = stalePage.waitForResponse((r) => withdrawUrl(withdrawnId).test(r.url()));
    await stalePage.getByRole('alertdialog').getByRole('button', { name: 'Withdraw dispute' }).click();
    const refused = await staleResponse;
    expect(refused.status()).toBe(409);
    const refusedBody = await refused.json();
    expect(expectProblemShape(refusedBody, 'action-not-allowed', 409)).toEqual([]);
    expect(refusedBody.customerStatus).toBe('withdrawn');
    await expect(stalePage.getByRole('alertdialog')).toHaveCount(0);
    await expect(stalePage.getByText('Withdrawn').first()).toBeVisible();
    await expect(stalePage.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
    await expect(stalePage.getByRole('heading', { level: 1 })).toBeFocused();
    await expect(stalePage.locator('ol li').filter({ hasText: 'second try' })).toHaveCount(0);
    expect(queryDatabase(`select count(*) from "StoredEvents" where "DisputeId" = '${withdrawnId}'`)).toBe('2');
    expect(consoleErrors).toEqual([]);
    await stalePage.close();
    await page.close();
  });

  test('criterion 7: after a 401 the sign-in return URL holds /disputes and no dispute ID, in any letter case', async () => {
    for (const path of [`/disputes/${openId}`, `/Disputes/${openId.toUpperCase()}`]) {
      const context = await signIn(await thirdContext.browser()!, 'customer-b');
      const page = await context.newPage();
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(openDescription);
      await page.getByRole('button', { name: 'Withdraw dispute' }).click();
      await context.clearCookies();
      await page.getByRole('alertdialog').getByRole('button', { name: 'Withdraw dispute' }).click();
      await page.waitForURL(/\/signed-out/);
      const url = new URL(page.url());
      expect(url.searchParams.get('returnUrl'), path).toBe('/disputes');
      expect(url.href).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}/i);
      await context.close();
    }
    // A 401 on the detail load itself (reached from the list through the SPA).
    const context = await signIn(await thirdContext.browser()!, 'customer-b');
    const page = await context.newPage();
    await page.goto('/disputes');
    await page.locator(`a[href="/disputes/${openId}"]`).first().waitFor();
    await context.clearCookies();
    await page.locator(`a[href="/disputes/${openId}"]`).first().click();
    await page.waitForURL(/\/signed-out/);
    expect(new URL(page.url()).searchParams.get('returnUrl')).toBe('/disputes');
    await context.close();
  });
});
