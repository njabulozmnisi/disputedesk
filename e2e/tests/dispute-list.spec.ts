import { execFileSync } from 'node:child_process';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase } from './support';

// Story 3.9: a customer lists their disputes newest first, a page at a time (GET /api/v1/disputes).
// Network level (the list screen is story 3.7), signed in through the hosted Zitadel login as the seeded customers.
// A and B are linked, C is not. Database facts come through qa_readonly. Two disputes cannot be given the same
// CreatedAtUtc through the API (the read-only role cannot write), so the tied-instant case rests on the integration tests.
// The data is not reset between runs: the spec tops A up to three disputes and B to one, raising only what is missing.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const dashboard = process.env.ASPIRE_ORIGIN ?? 'http://localhost:18888';
const contexts: Record<string, BrowserContext> = {};
const itemKeys = ['createdAt', 'customerStatus', 'disputeId', 'disputedAmount', 'outcome', 'reason', 'transaction'];

type Item = { disputeId: string; createdAt: string; [key: string]: unknown };
type Page = { items: Item[]; nextCursor: string | null };

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

async function raiseOnFirstDisputable(api: APIRequestContext, accountRef: string) {
  const list = await api.get('/api/v1/transactions?limit=50');
  const row = ((await list.json()).items as { transactionId: string; accountRef: string; disputable: boolean }[])
    .find((item) => item.disputable && item.accountRef === accountRef);
  expect(row, `a disputable ${accountRef} transaction is left (recreate the volumes if not)`).toBeTruthy();
  const token = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const response = await api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"qa39-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: { accountRef, transactionId: row!.transactionId, reason: 'unrecognised', note: 'qa 3.9' },
    maxRedirects: 0,
  });
  expect(response.status()).toBe(201);
}

async function listAll(api: APIRequestContext, limit: number): Promise<Page[]> {
  const pages: Page[] = [];
  let cursor: string | null = null;
  do {
    const query: string = `limit=${limit}${cursor === null ? '' : `&cursor=${cursor}`}`;
    await pace();
    const response = await api.get(`/api/v1/disputes?${query}`);
    expect(response.status()).toBe(200);
    const page = (await response.json()) as Page;
    pages.push(page);
    cursor = page.nextCursor;
    expect(pages.length).toBeLessThan(200);
  } while (cursor !== null);
  return pages;
}

const cursorOf = (instant: string, id: string) => Buffer.from(`${instant}|${id}`, 'utf8').toString('base64url');
const decodeCursor = (cursor: string) => Buffer.from(cursor, 'base64url').toString('utf8');
const withoutTraceId = ({ traceId: _ignored, ...rest }: Record<string, unknown>) => rest;
const auditCount = () => Number(queryDatabase('select count(*) from "AuditLogEntries"'));
const customerOf = (disputeId: string) => queryDatabase(`select "CustomerId" from "Disputes" where "Id" = '${disputeId}'`);

// The gateway limits /api/ to 10 requests a second with a burst of 20 (infra/nginx), so loops pace themselves.
const pace = () => new Promise((resolve) => setTimeout(resolve, 150));

let aItems: Item[];
let bItems: Item[];

test.beforeAll(async ({ browser }) => {
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');
  for (let have = (await listAll(contexts.a.request, 50)).flatMap((p) => p.items).length; have < 3; have++) {
    await raiseOnFirstDisputable(contexts.a.request, 'ACC-A-1');
  }
  if ((await listAll(contexts.b.request, 50)).flatMap((p) => p.items).length < 1) {
    await raiseOnFirstDisputable(contexts.b.request, 'ACC-B-1');
  }
  aItems = (await listAll(contexts.a.request, 50)).flatMap((p) => p.items);
  bItems = (await listAll(contexts.b.request, 50)).flatMap((p) => p.items);
});

test.beforeEach(async () => {
  await new Promise((resolve) => setTimeout(resolve, 3_000));
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

test('the list is newest first by createdAt then ID, exactly the stored rows of the caller, with only the allowed fields', async () => {
  const response = await contexts.a.request.get('/api/v1/disputes');
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  expect(response.headers()['content-type']).toContain('application/json');
  const page = (await response.json()) as Page;
  expect(Object.keys(page).sort()).toEqual(['items', 'nextCursor']);
  expect(page.nextCursor).toBeNull();
  expect(page.items.length).toBe(aItems.length);

  const customerId = customerOf(page.items[0].disputeId);
  const stored = queryDatabase(
    `select string_agg("Id"::text, ',' order by "CreatedAtUtc" desc, "Id" desc) from "Disputes" where "CustomerId" = '${customerId}'`);
  expect(page.items.map((item) => item.disputeId).join(',')).toBe(stored);

  for (const item of page.items) {
    await pace();
    expect(Object.keys(item).sort()).toEqual(itemKeys);
    expect(item.createdAt).toMatch(/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?\+00:00$/);
    expect(Object.keys(item.transaction as object).sort()).toEqual(
      Object.keys((await (await contexts.a.request.get(`/api/v1/disputes/${item.disputeId}`)).json()).transaction).sort());
  }
  // Each summary equals the detail on the shared fields.
  const first = page.items[0];
  const detail = await (await contexts.a.request.get(`/api/v1/disputes/${first.disputeId}`)).json();
  for (const key of itemKeys) expect(first[key]).toEqual(detail[key]);

  // Values: no internal strings anywhere in the page.
  const wire = JSON.stringify(page);
  for (const internal of ['CUS-', 'worker:', 'DisputeRaised', 'escalated', 'SlaDueAtUtc', 'ActorId', 'SlaWarningRaised', 'customerId']) {
    expect(wire).not.toContain(internal);
  }
  const statuses = queryDatabase(`select string_agg(distinct "Status", ',') from "Disputes" where "CustomerId" = '${customerId}'`);
  expect(statuses).toContain('Submitted');
  expect(page.items.every((item) => item.customerStatus === 'submitted' || typeof item.customerStatus === 'string')).toBe(true);
});

test('paging by cursor shows every dispute once in the same order as the full list, and the last page has no cursor', async () => {
  const full = aItems.map((item) => item.disputeId);
  for (const limit of [1, 2, aItems.length, aItems.length - 1]) {
    const pages = await listAll(contexts.a.request, limit);
    const ids = pages.flatMap((page) => page.items.map((item) => item.disputeId));
    expect(ids, `limit ${limit}`).toEqual(full);
    expect(new Set(ids).size).toBe(ids.length);
    expect(pages[pages.length - 1].nextCursor).toBeNull();
    for (const page of pages.slice(0, -1)) {
      expect(page.items).toHaveLength(limit);
      const last = page.items[page.items.length - 1];
      const [instant, cursorId] = decodeCursor(page.nextCursor!).split('|');
      expect(instant).toMatch(/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{7}\+00:00$/);
      expect(Date.parse(instant)).toBe(Date.parse(last.createdAt));
      expect(cursorId).toBe(last.disputeId);
    }
  }
  // A full last page (limit equal to the count) advertises no cursor.
  const exact = await (await contexts.a.request.get(`/api/v1/disputes?limit=${aItems.length}`)).json();
  expect(exact.nextCursor).toBeNull();
  const short = await (await contexts.a.request.get(`/api/v1/disputes?limit=${aItems.length - 1}`)).json();
  expect(short.nextCursor).not.toBeNull();
});

test('limit and cursor refusals are 400 validation naming the field, echo nothing, and every bad cursor gets one identical body', async () => {
  const get = async (query: string) => {
    await pace();
    return contexts.a.request.get(`/api/v1/disputes?${query}`);
  };
  for (const limit of ['0', '-1', '51', '2147483648', 'abc']) {
    const response = await get(`limit=${limit}`);
    expect(response.status(), `limit=${limit}`).toBe(400);
    const body = await response.json();
    expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
    expect(JSON.stringify(body.errors ?? body)).toContain('limit');
    expect(JSON.stringify(withoutTraceId(body))).not.toContain('abc');
    expect(JSON.stringify(withoutTraceId(body))).not.toContain('2147483648');
  }
  const id = aItems[0].disputeId;
  const good = '2026-10-01T10:00:00.0000000+00:00';
  const badCursors = [
    'a'.repeat(129),
    '***not base64url***',
    cursorOf('no-pipe-here', '').slice(0, 10),
    Buffer.from('nopipe', 'utf8').toString('base64url'),
    cursorOf('not-an-instant', id),
    cursorOf('2026-10-01T10:00:00.0000000+02:00', id),
    cursorOf(good, 'not-a-guid'),
    cursorOf(good, id.replace(/-/g, '')),
    cursorOf(good, `${id}|extra`),
  ];
  let reference: string | undefined;
  for (const cursor of badCursors) {
    const response = await get(`cursor=${cursor}`);
    expect(response.status(), `cursor ${cursor.slice(0, 12)}`).toBe(400);
    const body = await response.json();
    expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
    expect(JSON.stringify(body)).not.toContain(cursor);
    const comparable = JSON.stringify(withoutTraceId(body));
    expect(comparable).toContain('cursor');
    // The 129-character cursor is refused by built-in [MaxLength] validation with the framework's message (accepted in
    // 3.9-verify, round 2); the eight layout failures all come from DisputeCursor.Decode and share one body.
    if (cursor.length > 128) continue;
    reference ??= comparable;
    expect(comparable).toBe(reference);
  }
  // Well-formed extreme instants are never a 500.
  for (const instant of ['0001-01-01T00:00:00.0000000+00:00', '9999-12-31T23:59:59.9999999+00:00', '2026-10-01T10:00:00.1234567+00:00']) {
    const response = await get(`cursor=${cursorOf(instant, id)}`);
    expect([200, 400], instant).toContain(response.status());
  }
});

test("customer B's disputes never appear for A, with A's or B's customer ID in the query or a header, or a cursor on B's dispute", async () => {
  const aIds = new Set(aItems.map((item) => item.disputeId));
  const bIds = new Set(bItems.map((item) => item.disputeId));
  for (const id of bIds) expect(aIds.has(id)).toBe(false);
  expect(customerOf(bItems[0].disputeId)).not.toBe(customerOf(aItems[0].disputeId));

  const bCustomerId = customerOf(bItems[0].disputeId);
  const aCustomerId = customerOf(aItems[0].disputeId);
  const plain = await (await contexts.a.request.get('/api/v1/disputes')).json();
  for (const query of [`customerId=${bCustomerId}`, `customerId=${aCustomerId}`]) {
    const response = await contexts.a.request.get(`/api/v1/disputes?${query}`, { headers: { 'X-Customer-Id': bCustomerId, 'Customer-Id': bCustomerId } });
    expect(response.status()).toBe(200);
    expect(await response.json()).toEqual(plain);
  }

  // A cursor on B's dispute is a position only: 200, only A's rows, the same answer as for a position no dispute holds.
  const bRow = JSON.parse(queryDatabase(
    `select json_build_object('at', to_char("CreatedAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') || '0+00:00', 'id', "Id") from "Disputes" where "Id" = '${bItems[0].disputeId}'`));
  const onB = await contexts.a.request.get(`/api/v1/disputes?cursor=${cursorOf(bRow.at, bRow.id)}&limit=50`);
  const onNothing = await contexts.a.request.get(`/api/v1/disputes?cursor=${cursorOf(bRow.at, '00000000-0000-0000-0000-000000000000')}&limit=50`);
  expect(onB.status()).toBe(200);
  const onBPage = (await onB.json()) as Page;
  expect(onBPage.items.every((item) => aIds.has(item.disputeId))).toBe(true);
  const expected = aItems.filter((item) => Date.parse(item.createdAt) < Date.parse(bRow.at)).map((item) => item.disputeId);
  expect(onBPage.items.map((item) => item.disputeId)).toEqual(expected);
  expect(JSON.stringify(onBPage)).not.toContain(bRow.id);
  expect(onNothing.status()).toBe(200);
  expect(((await onNothing.json()) as Page).items.map((item) => item.disputeId)).toEqual(onBPage.items.map((item) => item.disputeId));

  // And B sees only B's rows.
  const bList = (await listAll(contexts.b.request, 1)).flatMap((page) => page.items.map((item) => item.disputeId));
  expect(bList).toEqual(bItems.map((item) => item.disputeId));
  for (const id of bList) expect(aIds.has(id)).toBe(false);
});

test('authorization states: 401, then 403, then 400; bearer alone is 401; no call writes an audit row', async ({ playwright }) => {
  const before = auditCount();
  const anonymous = await playwright.request.newContext({ baseURL: origin });
  const anonymousResponse = await anonymous.get('/api/v1/disputes', { maxRedirects: 0 });
  expect(anonymousResponse.status()).toBe(401);
  expect(anonymousResponse.headers()['www-authenticate']).toBeUndefined();
  expect(anonymousResponse.headers()['location']).toBeUndefined();
  expect(expectProblemShape(await anonymousResponse.json(), 'unauthorized', 401)).toEqual([]);
  expect((await anonymous.get('/api/v1/disputes?limit=51', { maxRedirects: 0 })).status()).toBe(401);
  const bearer = await anonymous.get('/api/v1/disputes', { headers: { Authorization: 'Bearer a.b.c' }, maxRedirects: 0 });
  expect(bearer.status()).toBe(401);
  await anonymous.dispose();

  const unlinked = await contexts.c.request.get('/api/v1/disputes');
  expect(unlinked.status()).toBe(403);
  expect((await unlinked.json()).type).toMatch(/linking-required\.md$/);
  expect((await contexts.c.request.get('/api/v1/disputes?limit=51')).status()).toBe(403);
  expect((await contexts.c.request.get(`/api/v1/disputes?cursor=${'a'.repeat(129)}`)).status()).toBe(403);

  expect((await contexts.a.request.get('/api/v1/disputes')).status()).toBe(200);
  expect(auditCount()).toBe(before);
});

test('the list answers 200 from the stored snapshots, unchanged, while core banking is down', async () => {
  const before = await (await contexts.a.request.get('/api/v1/disputes?limit=50')).text();
  execFileSync('docker', ['stop', 'disputedesk-core-banking-1']);
  try {
    expect((await contexts.a.request.get('/api/v1/transactions?limit=1')).status()).toBe(503);
    const response = await contexts.a.request.get('/api/v1/disputes?limit=50');
    expect(response.status()).toBe(200);
    expect(await response.text()).toBe(before);
  } finally {
    execFileSync('docker', ['start', 'disputedesk-core-banking-1']);
  }
  await expect.poll(async () => (await contexts.a.request.get('/api/v1/transactions?limit=1')).status(), { timeout: 60_000, intervals: [1000] }).toBe(200);
});

test('a customer ID in the query is not exported: the list span carries no customer ID in url.query or any attribute', async () => {
  const bCustomerId = customerOf(bItems[0].disputeId);
  const traceId = Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16)).join('');
  const response = await contexts.a.request.get(`/api/v1/disputes?customerId=${bCustomerId}&limit=1`, {
    headers: { traceparent: `00-${traceId}-${'1'.repeat(16)}-01` },
  });
  expect(response.status()).toBe(200);
  let spans = '';
  await expect.poll(async () => {
    const result = await fetch(`${dashboard}/api/telemetry/spans?traceId=${traceId}`);
    spans = result.ok ? await result.text() : '';
    return spans.includes('/api/v1/disputes');
  }, { timeout: 30_000, intervals: [1000] }).toBe(true);
  expect(spans).not.toContain(bCustomerId);
  expect(spans).not.toContain(customerOf(aItems[0].disputeId));
  expect(spans).toContain('url.query');
});
