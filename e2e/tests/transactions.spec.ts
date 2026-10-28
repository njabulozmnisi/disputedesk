import { expect, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase } from './support';

// Story 2.3: a linked customer lists their own transactions. API level only (no SPA yet).
// Customers A and B are linked, C is not. Each signs in once through the hosted Zitadel login.

type Row = { transactionId: string; maskedCard: string | null; accountLast4: string; disputable: boolean; notDisputableReason: string | null };
type Page = { items: Row[]; nextCursor: string | null };

const contexts: Record<string, BrowserContext> = {};

async function signIn(browser: Browser, loginKey: string): Promise<BrowserContext> {
  const login = fixtureLogin(loginKey);
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/api/v1/bff/login');
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password);
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === 'http://localhost:8080');
  await page.close();
  return context;
}

async function listAll(api: APIRequestContext, limit: number): Promise<Row[]> {
  const rows: Row[] = [];
  let cursor: string | null = null;
  do {
    const query: string = `limit=${limit}` + (cursor ? `&cursor=${cursor}` : '');
    const response = await api.get(`/api/v1/transactions?${query}`);
    expect(response.status()).toBe(200);
    const page: Page = await response.json();
    rows.push(...page.items);
    cursor = page.nextCursor;
  } while (cursor);
  return rows;
}

test.beforeAll(async ({ browser }) => {
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

test('three authorisation states: 401, 403 linking-required, 200, never a redirect', async ({ request }) => {
  const anonymous = await request.get('/api/v1/transactions', { maxRedirects: 0 });
  expect(anonymous.status()).toBe(401);
  expect(expectProblemShape(await anonymous.json(), 'unauthorized', 401)).toEqual([]);

  const unlinked = await contexts.c.request.get('/api/v1/transactions', { maxRedirects: 0 });
  expect(unlinked.status()).toBe(403);
  expect(expectProblemShape(await unlinked.json(), 'linking-required', 403)).toEqual([]);

  const linked = await contexts.a.request.get('/api/v1/transactions', { maxRedirects: 0 });
  expect(linked.status()).toBe(200);
  expect(linked.headers()['cache-control']).toBe('no-store');
});

test('session state reads the link: anonymous, unlinked, linked', async ({ request }) => {
  expect(await (await request.get('/api/v1/bff/session')).json()).toEqual({ state: 'anonymous' });
  expect(await (await contexts.c.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'unlinked' });
  expect(await (await contexts.a.request.get('/api/v1/bff/session')).json()).toEqual({ state: 'linked' });
});

test('default page is 20, paging reaches the end, every page size gives the same newest-first list', async () => {
  const first = await (await contexts.a.request.get('/api/v1/transactions')).json();
  expect(first.items).toHaveLength(20);
  expect(first.nextCursor).not.toBeNull();

  const byTwenty = await listAll(contexts.a.request, 20);
  const bySeven = await listAll(contexts.a.request, 7);
  const byFifty = await listAll(contexts.a.request, 50);
  expect(byTwenty).toHaveLength(30);
  expect(new Set(byTwenty.map((row) => row.transactionId)).size).toBe(30);
  expect(bySeven.map((row) => row.transactionId)).toEqual(byTwenty.map((row) => row.transactionId));
  expect(byFifty.map((row) => row.transactionId)).toEqual(byTwenty.map((row) => row.transactionId));
});

test('customer A never sees B rows, and a cursor from one customer is refused for the other', async () => {
  const aRows = await listAll(contexts.a.request, 20);
  const bRows = await listAll(contexts.b.request, 20);
  expect(aRows.every((row) => row.transactionId.startsWith('TXN-A-'))).toBe(true);
  expect(bRows).toHaveLength(28);
  expect(bRows.every((row) => row.transactionId.startsWith('TXN-B-'))).toBe(true);

  const bPage = await (await contexts.b.request.get('/api/v1/transactions?limit=5')).json();
  const crossed = await contexts.a.request.get(`/api/v1/transactions?cursor=${bPage.nextCursor}`);
  expect(crossed.status()).toBe(400);
  expect(expectProblemShape(await crossed.json(), 'validation', 400)).toEqual([]);

  // Neither a query field nor a header chooses the customer.
  const forged = await contexts.a.request.get('/api/v1/transactions?customerId=CUS-000002', {
    headers: { 'X-Customer-Id': 'CUS-000002' },
  });
  const forgedRows: Row[] = (await forged.json()).items;
  expect(forgedRows.every((row) => row.transactionId.startsWith('TXN-A-'))).toBe(true);
});

test('limit above 50 and an undecodable cursor are 400 validation naming the field', async () => {
  const tooMany = await contexts.a.request.get('/api/v1/transactions?limit=51');
  expect(tooMany.status()).toBe(400);
  expect((await tooMany.json()).errors).toHaveProperty('limit');

  const badCursor = await contexts.a.request.get('/api/v1/transactions?cursor=garbage');
  expect(badCursor.status()).toBe(400);
  expect((await badCursor.json()).errors).toHaveProperty('cursor');
});

// Found by QA: a non-numeric limit is 500 `internal` today (BadHttpRequestException from parameter binding).
test('a non-numeric limit is 400 validation, not a server error', async () => {
  const response = await contexts.a.request.get('/api/v1/transactions?limit=abc');
  expect(response.status()).toBe(400);
  expect(expectProblemShape(await response.json(), 'validation', 400)).toEqual([]);
});

test('row fields: money as string, masked card and account, camelCase enums, disputability', async () => {
  const rows = await listAll(contexts.a.request, 50);
  // No full account, card or ID number anywhere. A dispute ID is a UUID whose random tail can hold ten digits, so UUIDs are removed first.
  const json = JSON.stringify(rows).replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g, '');
  expect(json).not.toMatch(/\d{10}/);
  for (const row of rows) {
    expect(row.accountLast4).toMatch(/^\d{4}$/);
    if (row.maskedCard) expect(row.maskedCard).toMatch(/^\d{6}\*{6}\d{4}$/);
  }
  expect(rows[0]).toMatchObject({ amount: { amount: expect.stringMatching(/^\d+\.\d{2}$/), currency: 'ZAR' } });

  const disputable = rows.filter((row) => row.disputable);
  expect(disputable.length).toBeGreaterThan(0);
  expect(disputable.every((row) => row.notDisputableReason === null)).toBe(true);
  const pending = rows.find((row: any) => row.state === 'pending');
  expect(pending).toMatchObject({ disputable: false, notDisputableReason: 'notEligible' });
  const reasons = new Set(rows.filter((row) => !row.disputable).map((row) => row.notDisputableReason));
  // Story 3.1 added alreadyDisputed, which appears once the customer has raised a dispute (the data is not reset between runs).
  expect(reasons.has('notEligible') && reasons.has('outsideWindow')).toBe(true);
  expect([...reasons].filter((reason) => !['notEligible', 'outsideWindow', 'alreadyDisputed'].includes(reason as string))).toEqual([]);
});

test('CustomerLinks holds exactly customers A and B', async () => {
  expect(queryDatabase('select string_agg("CustomerId", \',\' order by "CustomerId") from "CustomerLinks"'))
    .toBe('CUS-000001,CUS-000002');
});
