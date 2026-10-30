import { expect, request as playwrightRequest, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, ownDisputable, queryDatabase } from './support';

// Story 3.4: a customer withdraws an open dispute. The SPA has no withdraw screen yet, so the withdraw is sent at network
// level from browser contexts that signed in through Zitadel (session cookie and antiforgery token as a browser sends them).
// Every database fact is read through the read-only qa_readonly role. There is no outbox relay before story 4.1, so the
// events hop is the outbox row. A Resolved dispute cannot be made on the stack (no resolve action exists yet), so the
// resolved-409 cell is covered by the integration tests only. A transaction is disputed once: the spec raises on its
// reserved transactions (support.ts): eight of customer A's, one of customer B's.

type Row = { transactionId: string; accountRef: string; state: string; disputable: boolean };

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const contexts: Record<string, BrowserContext> = {};

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

async function transactions(api: APIRequestContext): Promise<Row[]> {
  const response = await api.get('/api/v1/transactions?limit=50');
  expect(response.status()).toBe(200);
  return (await response.json()).items as Row[];
}

async function token(api: APIRequestContext): Promise<string> {
  // The gateway's coarse limit is 10 requests a second per address (burst 20); every call here fetches a token first.
  await new Promise((resolve) => setTimeout(resolve, 150));
  return (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
}

async function raise(api: APIRequestContext, row: Row) {
  const response = await api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': await token(api), 'Idempotency-Key': `"qa34-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: { accountRef: row.accountRef, transactionId: row.transactionId, reason: 'unrecognised', note: 'Raised by the withdraw QA.' },
    maxRedirects: 0,
  });
  return response;
}

async function raiseOpen(api: APIRequestContext, row: Row): Promise<string> {
  const response = await raise(api, row);
  expect(response.status()).toBe(201);
  return (await response.json()).disputeId as string;
}

type Body = { json?: unknown; raw?: string; contentType?: string; header?: boolean };

/** A withdraw as the SPA will send it. `raw` sends the text as is; `json` sends a JSON body; neither sends no body. */
async function withdraw(api: APIRequestContext, disputeId: string, body: Body = {}) {
  const headers: Record<string, string> = {};
  if (body.header !== false) headers['X-XSRF-TOKEN'] = await token(api);
  const options: { headers: Record<string, string>; data?: unknown; maxRedirects: number } = { headers, maxRedirects: 0 };
  if (body.raw !== undefined) {
    headers['Content-Type'] = body.contentType ?? 'application/json';
    options.data = body.raw;
  } else if (body.json !== undefined) {
    headers['Content-Type'] = 'application/json';
    options.data = JSON.stringify(body.json);
  }
  return api.post(`/api/v1/disputes/${disputeId}/withdraw`, options);
}

function rows<T>(sql: string): T[] {
  return JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
}

const totals = () => rows<{ disputes: number; stored: number; outbox: number; audit: number }>(
  `select (select count(*) from "Disputes") as disputes, (select count(*) from "StoredEvents") as stored,
          (select count(*) from "OutboxMessages") as outbox, (select count(*) from "AuditLogEntries") as audit`)[0];

const instant = (value: string) => value.replace(/(\+00:00|Z)$/, '').replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '');

const stream = (id: string) => rows<any>(`select "Id", "Version", "EventType", "ActorType", "ActorId", "OccurredAtUtc", "Data" from "StoredEvents" where "DisputeId" = '${id}' order by "Version"`);
const disputeRow = (id: string) => rows<any>(`select "Status", "Version", "Note", "CustomerId" from "Disputes" where "Id" = '${id}'`)[0];
const auditFor = (sql: string) => rows<any>(`select "Action", "Result", "ActorType", "ActorId", "CustomerId", "DisputeId", "TraceId" from "AuditLogEntries" where ${sql} order by "OccurredAtUtc"`);

let aFree: Row[];
let bFree: Row[];
let aPending: Row;
let main: { id: string; row: Row; body: any };

test.beforeAll(async ({ browser }) => {
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');
  const a = await transactions(contexts.a.request);
  aFree = ownDisputable(a, 'dispute-withdraw', 'a');
  aPending = a.find((row) => row.state === 'pending')!;
  bFree = ownDisputable(await transactions(contexts.b.request), 'dispute-withdraw', 'b');
  expect(aFree.length, 'the spec\'s eight reserved customer A transactions are disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(8);
  expect(bFree.length, 'the spec\'s reserved customer B transaction is disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(1);
  expect(aPending, 'customer A has a pending transaction').toBeTruthy();
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

const a = () => contexts.a.request;

test.describe.serial('withdraw an own open dispute', () => {
  test('200 equals the following GET, with the withdraw as the new last timeline item', async () => {
    const row = aFree.shift()!;
    const id = await raiseOpen(a(), row);
    const before = totals();
    const response = await withdraw(a(), id, { json: { note: '  I changed my mind.  ' } });
    expect(response.status()).toBe(200);
    const body = await response.json();
    const get = await a().get(`/api/v1/disputes/${id}`);
    expect(get.status()).toBe(200);
    expect(body).toEqual(await get.json());
    expect(body.customerStatus).toBe('withdrawn');
    expect(body.note).toBe('Raised by the withdraw QA.');
    expect(body.timeline).toHaveLength(2);
    expect(body.timeline[1]).toMatchObject({ action: 'withdraw', customerStatus: 'withdrawn', actor: 'you', note: 'I changed my mind.' });
    expect(JSON.stringify(body)).not.toMatch(/CUS-\d/);
    main = { id, row, body };
    expect(totals()).toEqual({ disputes: before.disputes, stored: before.stored + 1, outbox: before.outbox + 1, audit: before.audit });
  });

  test('database: Disputes row Withdrawn at Version 2, one DisputeWithdrawn event, one outbox row with the same ID, no audit row', async () => {
    const events = stream(main.id);
    expect(events.map((event) => event.EventType)).toEqual(['DisputeRaised', 'DisputeWithdrawn']);
    expect(events[1]).toMatchObject({ Version: 2, ActorType: 'Customer' });
    expect(events[1].ActorId).toBe(events[0].ActorId);
    expect(events[1].Data.note).toBe('I changed my mind.');
    expect(disputeRow(main.id)).toMatchObject({ Status: 'Withdrawn', Version: 2 });

    const outbox = rows<any>(`select "Id", "EventType", "DisputeVersion", "Status", "TraceParent", "Payload" from "OutboxMessages" where "DisputeId" = '${main.id}' and "DisputeVersion" = 2`);
    expect(outbox).toHaveLength(1);
    expect(outbox[0]).toMatchObject({ Id: events[1].Id, EventType: 'disputedesk.dispute.status-changed.v1', Status: 'Pending' });
    const payload = JSON.parse(outbox[0].Payload);
    expect(payload).toMatchObject({ specversion: '1.0', type: 'disputedesk.dispute.status-changed.v1', id: events[1].Id, subject: main.id });
    expect(payload.data).toEqual({ disputeId: main.id, fromStatus: 'submitted', toStatus: 'withdrawn', action: 'withdraw', actorType: 'customer', occurredAt: payload.data.occurredAt, disputeVersion: 2 });
    expect(outbox[0].Payload).not.toContain('I changed my mind');
    expect(outbox[0].TraceParent.split('-')[1]).toMatch(/^[0-9a-f]{32}$/);
    expect(instant(payload.data.occurredAt)).toBe(instant(events[1].OccurredAtUtc));
    expect(instant(main.body.timeline[1].occurredAt)).toBe(instant(events[1].OccurredAtUtc));
    expect(auditFor(`"DisputeId" = '${main.id}'`)).toHaveLength(0);
  });

  test('the transaction stays not disputable, and a second raise is 409 already-disputed', async () => {
    const row = (await transactions(a())).find((candidate) => candidate.transactionId === main.row.transactionId)!;
    expect(row.disputable).toBe(false);
    const again = await raise(a(), main.row);
    expect(again.status()).toBe(409);
    const body = await again.json();
    expect(expectProblemShape(body, 'already-disputed', 409)).toEqual([]);
    expect(body.disputeId).toBe(main.id);
  });

  test('a second withdraw is 409 action-not-allowed with customerStatus withdrawn; one event, one outbox row', async () => {
    const before = totals();
    const response = await withdraw(a(), main.id, { json: {} });
    expect(response.status()).toBe(409);
    const body = await response.json();
    expect(expectProblemShape(body, 'action-not-allowed', 409)).toEqual([]);
    expect(body.customerStatus).toBe('withdrawn');
    expect(stream(main.id).filter((event) => event.EventType === 'DisputeWithdrawn')).toHaveLength(1);
    expect(rows(`select 1 from "OutboxMessages" where "DisputeId" = '${main.id}' and "DisputeVersion" = 2`)).toHaveLength(1);
    const after = totals();
    expect(after).toEqual({ ...before, audit: before.audit + 1 });
  });

  test('both refusals (the second raise and the second withdraw) are audited Rejected with every column, on A\'s own dispute', async () => {
    const [raised] = stream(main.id);
    const entries = auditFor(`"DisputeId" = '${main.id}'`);
    expect(entries.map((entry) => entry.Action).sort()).toEqual(['Raise', 'Withdraw']);
    for (const entry of entries) {
      expect(entry).toMatchObject({ Result: 'Rejected', ActorType: 'Customer', ActorId: raised.ActorId, CustomerId: raised.Data.customerId, DisputeId: main.id });
      expect(entry.TraceId).toMatch(/^[0-9a-f]{32}$/);
    }
  });
});

test.describe.serial('note bounds', () => {
  const successNote = async (json: unknown, expected: string | null) => {
    const id = await raiseOpen(a(), aFree.shift()!);
    const response = await withdraw(a(), id, { json });
    expect(response.status()).toBe(200);
    const body = await response.json();
    expect(body.timeline[1].note).toBe(expected);
    expect(disputeRow(id)).toMatchObject({ Status: 'Withdrawn', Version: 2 });
    expect(stream(id)[1].Data.note ?? null).toBe(expected);
    return id;
  };

  test('500 code units succeed, 500 padded with spaces succeed, 501 is 400 validation naming note', async () => {
    await successNote({ note: 'x'.repeat(500) }, 'x'.repeat(500));
    const id = await raiseOpen(a(), aFree.shift()!);
    const before = totals();
    const tooLong = await withdraw(a(), id, { json: { note: 'y'.repeat(501) } });
    expect(tooLong.status()).toBe(400);
    const body = await tooLong.json();
    expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
    expect(Object.keys(body.errors)).toEqual(['note']);
    expect(JSON.stringify(body.errors)).toContain('500');
    expect(JSON.stringify(body.errors)).not.toContain('1000');
    expect(totals()).toEqual(before); // a post-load 400 writes nothing, not even an audit row
    expect(disputeRow(id)).toMatchObject({ Status: 'Submitted', Version: 1 });
    // 501 units that trim to 500 are fine: the same dispute, now padded.
    const padded = await withdraw(a(), id, { json: { note: `   ${'z'.repeat(500)}   ` } });
    expect(padded.status()).toBe(200);
    expect((await padded.json()).timeline[1].note).toBe('z'.repeat(500));
  });

  test('whitespace-only, {} and null mean no note', async () => {
    await successNote({ note: '   \t ' }, null);
    await successNote({}, null);
    await successNote({ note: null }, null);
  });
});

test.describe.serial('binding failures answer 400 before the lookup', () => {
  let openId: string;
  const unknownId = '5b8c1c64-0f2f-4a7e-9d44-7a1b1f2b3c4d';

  test('on an open dispute and on an unknown ID alike: 400 validation with no field named', async () => {
    openId = await raiseOpen(a(), aFree.shift()!);
    const cases: { name: string; body: Body }[] = [
      { name: 'no body', body: {} },
      { name: 'null', body: { raw: 'null' } },
      { name: 'array', body: { raw: '[]' } },
      { name: 'malformed JSON', body: { raw: '{"note": ' } },
      { name: 'note is a number', body: { raw: '{"note": 5}' } },
    ];
    const before = totals();
    for (const target of [openId, unknownId]) {
      for (const { name, body } of cases) {
        const response = await withdraw(a(), target, body);
        expect(response.status(), name).toBe(400);
        const problem = await response.json();
        expect(expectProblemShape(problem, 'validation', 400), name).toEqual([]);
        expect(problem.errors ?? {}, name).toEqual({});
      }
    }
    const notGuid = await withdraw(a(), 'not-a-guid', { json: {} });
    expect(notGuid.status()).toBe(400);
    expect(expectProblemShape(await notGuid.json(), 'validation', 400)).toEqual([]);
    expect(totals()).toEqual(before);
    expect(disputeRow(openId)).toMatchObject({ Status: 'Submitted', Version: 1 });
  });

  test('a text/plain body is 415 validation', async () => {
    const response = await withdraw(a(), openId, { raw: '{"note":"x"}', contentType: 'text/plain' });
    expect(response.status()).toBe(415);
    expect(expectProblemShape(await response.json(), 'validation', 415)).toEqual([]);
  });

  test('a 501-unit note gets 404 on an unknown ID, and 400 on the owner\'s dispute', async () => {
    const unknown = await withdraw(a(), unknownId, { json: { note: 'q'.repeat(501) } });
    expect(unknown.status()).toBe(404);
    const known = await withdraw(a(), openId, { json: { note: 'q'.repeat(501) } });
    expect(known.status()).toBe(400);
  });

  test('unknown properties are ignored', async () => {
    const response = await withdraw(a(), openId, { json: { note: 'ok', status: 'resolved', outcome: 'accepted' } });
    expect(response.status()).toBe(200);
    expect((await response.json()).customerStatus).toBe('withdrawn');
    expect(disputeRow(openId)).toMatchObject({ Status: 'Withdrawn', Version: 2 });
  });
});

test.describe.serial('only the owner, only through the cookie', () => {
  let bId: string;
  let bBefore: { stream: any[]; row: any };
  let bearer: APIRequestContext;

  test.beforeAll(async () => {
    bId = await raiseOpen(contexts.b.request, bFree.shift()!);
    bBefore = { stream: stream(bId), row: disputeRow(bId) };
    bearer = await playwrightRequest.newContext({ baseURL: origin, extraHTTPHeaders: { Authorization: 'Bearer not-a-real-token' } });
  });

  test.afterAll(async () => {
    await bearer.dispose();
  });

  test('a bearer token is 401, an unlinked login 403 linking-required, a cookie without the header 400 before any lookup', async () => {
    const before = totals();
    const unauthorised = await bearer.post(`/api/v1/disputes/${bId}/withdraw`, { data: {}, maxRedirects: 0 });
    expect(unauthorised.status()).toBe(401);
    expect(expectProblemShape(await unauthorised.json(), 'unauthorized', 401)).toEqual([]);

    const unlinked = await withdraw(contexts.c.request, bId, { json: {} });
    expect(unlinked.status()).toBe(403);
    expect(expectProblemShape(await unlinked.json(), 'linking-required', 403)).toEqual([]);

    const noHeader = await withdraw(a(), bId, { json: {}, header: false });
    expect(noHeader.status()).toBe(400);
    expect(expectProblemShape(await noHeader.json(), 'validation', 400)).toEqual([]);
    // Without the header an unknown ID is also 400, not 404: the antiforgery check comes first.
    const noHeaderUnknown = await withdraw(a(), '5b8c1c64-0f2f-4a7e-9d44-7a1b1f2b3c4d', { json: {}, header: false });
    expect(noHeaderUnknown.status()).toBe(400);
    expect(totals()).toEqual(before); // none of these writes an audit row
  });

  test('A withdrawing B\'s dispute and an unknown ID give the identical 404, with and without a 501-unit note', async () => {
    const unknownId = '0c2f6b1e-8a3d-4d57-b1a4-3f6e9c7d5e21';
    for (const json of [{ note: 'because' }, { note: 'q'.repeat(501) }]) {
      const foreign = await withdraw(a(), bId, { json });
      const unknown = await withdraw(a(), unknownId, { json });
      expect(foreign.status()).toBe(404);
      expect(unknown.status()).toBe(404);
      const foreignBody = await foreign.json();
      const unknownBody = await unknown.json();
      expect(expectProblemShape(foreignBody, 'not-found', 404)).toEqual([]);
      const { traceId: t1, ...foreignRest } = foreignBody;
      const { traceId: t2, ...unknownRest } = unknownBody;
      expect(foreignRest).toEqual(unknownRest);
      expect(JSON.stringify(foreignBody)).not.toContain(bId);
    }
    // B's stream, row and outbox are untouched.
    expect(stream(bId)).toEqual(bBefore.stream);
    expect(disputeRow(bId)).toEqual(bBefore.row);
    expect(rows(`select 1 from "OutboxMessages" where "DisputeId" = '${bId}'`)).toHaveLength(1);
    expect(disputeRow(bId)).toMatchObject({ Status: 'Submitted', Version: 1 });
  });

  test('both 404s are audited for A with no foreign IDs, and the rows are equal apart from Id, OccurredAtUtc and TraceId', async () => {
    const aRaised = stream(main.id)[0];
    const aCustomer = aRaised.Data.customerId as string;
    const aSub = aRaised.ActorId as string;
    const bCustomer = bBefore.stream[0].Data.customerId as string;
    expect(aCustomer).not.toBe(bCustomer);
    const entries = rows<any>(`select "Action", "Result", "ActorType", "ActorId", "CustomerId", "DisputeId" from "AuditLogEntries" where "Action" = 'Withdraw' and "Result" = 'Rejected' and "DisputeId" is null and "CustomerId" = '${aCustomer}'`);
    expect(entries.length).toBeGreaterThanOrEqual(4);
    for (const entry of entries) expect(entry).toEqual(entries[0]);
    expect(entries[0]).toEqual({ Action: 'Withdraw', Result: 'Rejected', ActorType: 'Customer', ActorId: aSub, CustomerId: aCustomer, DisputeId: null });
    expect(rows(`select 1 from "AuditLogEntries" where "DisputeId" = '${bId}' or ("Action" = 'Withdraw' and ("CustomerId" = '${bCustomer}' or "ActorId" = '${bBefore.stream[0].ActorId}'))`)).toHaveLength(0);
  });
});

test.describe.serial('refused raises are audited Rejected', () => {
  test('404 not-found (unknown transaction) and 422 not-disputable (pending) are audited with a null DisputeId', async () => {
    const before = totals();
    const base = { accountRef: aPending.accountRef, reason: 'unrecognised' };
    const key = () => `"qa34-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"`;
    const unknown = await a().post('/api/v1/disputes', { headers: { 'X-XSRF-TOKEN': await token(a()), 'Idempotency-Key': key() }, data: { ...base, transactionId: 'TXN-A-9999' } });
    expect(unknown.status()).toBe(404);
    const pending = await a().post('/api/v1/disputes', { headers: { 'X-XSRF-TOKEN': await token(a()), 'Idempotency-Key': key() }, data: { ...base, transactionId: aPending.transactionId } });
    expect(pending.status()).toBe(422);
    expect(expectProblemShape(await pending.json(), 'not-disputable', 422)).toEqual([]);
    const after = totals();
    expect(after.audit - before.audit).toBe(2);
    expect(after.disputes).toBe(before.disputes);
    expect(after.stored).toBe(before.stored);
    expect(after.outbox).toBe(before.outbox);
    const entries = rows<any>(`select "Action", "Result", "ActorType", "CustomerId", "DisputeId", "TraceId" from "AuditLogEntries" where "Action" = 'Raise' order by "OccurredAtUtc" desc limit 2`);
    for (const entry of entries) {
      expect(entry).toMatchObject({ Action: 'Raise', Result: 'Rejected', ActorType: 'Customer', DisputeId: null });
      expect(entry.CustomerId).toBeTruthy();
      expect(entry.TraceId).toMatch(/^[0-9a-f]{32}$/);
    }
  });

  test('a validation 400 (expected amount not below the amount) is not audited', async () => {
    const before = totals();
    const row = aFree.shift()!;
    const response = await a().post('/api/v1/disputes', {
      headers: { 'X-XSRF-TOKEN': await token(a()), 'Idempotency-Key': `"qa34-${Date.now()}-v"` },
      data: { accountRef: row.accountRef, transactionId: row.transactionId, reason: 'wrongAmount', expectedAmount: 999999.0 },
    });
    expect(response.status()).toBe(400);
    expect(totals()).toEqual(before);
  });
});
