import { execFileSync } from 'node:child_process';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext, type Page } from '@playwright/test';
import { expectProblemShape, fixtureLogin, ownDisputable, queryDatabase } from './support';

// Story 3.8: a raise is one stored event, one Disputes row and one outbox row, written together, with no audit row.
// The SPA has no raise screen until story 3.6, so the raise is sent at network level from browser contexts that
// signed in through Zitadel (the session cookie and the antiforgery token travel as a browser would send them).
// Every database fact is read through the read-only qa_readonly role. There is no outbox relay before story 4.1, so
// the events hop is the outbox row; since story 4.1 the relay publishes it (outbox-relay.spec.ts reads the topic).
// A transaction is disputed once: the spec raises on its reserved transactions (support.ts).

type Row = { transactionId: string; accountRef: string; description: string; disputable: boolean };

const contexts: Record<string, BrowserContext> = {};
const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';

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

async function disputableRows(api: APIRequestContext, customer: 'a' | 'b'): Promise<Row[]> {
  const response = await api.get('/api/v1/transactions?limit=50');
  expect(response.status()).toBe(200);
  return ownDisputable((await response.json()).items as Row[], 'dispute-stored-events', customer);
}

/** One raise under the given key, with a fresh antiforgery token, as the SPA will send it. */
async function raise(api: APIRequestContext, key: string | null, body: object) {
  const token = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const headers: Record<string, string> = { 'X-XSRF-TOKEN': token };
  if (key !== null) headers['Idempotency-Key'] = `"${key}"`;
  return api.post('/api/v1/disputes', { headers, data: body, maxRedirects: 0 });
}

function rows<T>(sql: string): T[] {
  const text = queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`);
  return JSON.parse(text) as T[];
}

const totals = () => rows<{ disputes: number; stored: number; outbox: number; audit: number }>(
  `select (select count(*) from "Disputes") as disputes, (select count(*) from "StoredEvents") as stored,
          (select count(*) from "OutboxMessages") as outbox, (select count(*) from "AuditLogEntries") as audit`)[0];

/**
 * The same instant, digit for digit: only the offset suffix (Postgres "+00:00", .NET "Z") and trailing fraction zeros
 * (written differently by each) are normalised; nothing is truncated. Fixed in round 3 of story 3.8 (was finding L1).
 */
const instant = (value: string) => value.replace(/(\+00:00|Z)$/, '').replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '');
const fractionDigits = (value: string) => (instant(value).split('.')[1] ?? '').length;

const unique = () => `qa38-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;

let aRows: Row[];
let bRows: Row[];
let raisedByB: { disputeId: string; transaction: Row } | undefined; // read by the browser test, which needs a row on the SPA's first page
let raisedByA: { disputeId: string; body: any; key: string; request: object; transaction: Row };
let totalsBeforeRaise: ReturnType<typeof totals>;

test.beforeAll(async ({ browser }) => {
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');
  aRows = await disputableRows(contexts.a.request, 'a');
  bRows = await disputableRows(contexts.b.request, 'b');
  expect(aRows.length, 'two of the spec\'s reserved customer A transactions are disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(2);
  expect(bRows.length, 'the spec\'s reserved customer B transaction is disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(1);
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

test.describe.serial('raise as customer A', () => {
  test('201 body: submitted, one timeline entry, no extra fields', async () => {
    const transaction = aRows[0];
    const request = { accountRef: transaction.accountRef, transactionId: transaction.transactionId, reason: 'unrecognised', note: 'I did not make this purchase.' };
    const key = unique();
    totalsBeforeRaise = totals();
    const response = await raise(contexts.a.request, key, request);
    expect(response.status()).toBe(201);
    const body = await response.json();
    expect(response.headers()['location']).toBe(`/api/v1/disputes/${body.disputeId}`);
    expect(response.headers()['cache-control']).toBe('no-store');
    expect(Object.keys(body).sort()).toEqual(
      ['createdAt', 'customerStatus', 'disputeId', 'disputedAmount', 'expectedAmount', 'note', 'outcome', 'reason', 'timeline', 'transaction'].sort());
    expect(body.outcome).toBeNull(); // story 3.3: outcome is null until the dispute is resolved
    expect(body.customerStatus).toBe('submitted');
    expect(body.timeline).toHaveLength(1);
    expect(Object.keys(body.timeline[0]).sort()).toEqual(['action', 'actor', 'customerStatus', 'note', 'occurredAt']);
    expect(body.timeline[0]).toMatchObject({ action: 'raise', customerStatus: 'submitted', actor: 'you', note: request.note });
    // No identifier of the customer, the full card or the stored event leaks into the answer.
    expect(JSON.stringify(body)).not.toMatch(/CUS-\d/);
    raisedByA = { disputeId: body.disputeId, body, key, request, transaction };
  });

  test('database: one stored event, one read model row with no outcome, one outbox row with the same ID, one instant, one trace, no audit row', async () => {
    const id = raisedByA.disputeId;
    const stored = rows<any>(`select "Id", "Version", "EventType", "ActorType", "OccurredAtUtc", "TraceId", "Data" from "StoredEvents" where "DisputeId" = '${id}'`);
    expect(stored).toHaveLength(1);
    expect(stored[0]).toMatchObject({ Version: 1, EventType: 'DisputeRaised', ActorType: 'Customer' });

    const dispute = rows<any>(`select "Status", "Outcome", "Version", "CreatedAtUtc", "Reason", "Note", "TransactionId" from "Disputes" where "Id" = '${id}'`);
    expect(dispute).toHaveLength(1);
    expect(dispute[0]).toMatchObject({ Status: 'Submitted', Outcome: null, Version: 1, Reason: 'Unrecognised', TransactionId: raisedByA.transaction.transactionId });

    const outbox = rows<any>(`select "Id", "EventType", "DisputeId", "DisputeVersion", "Status", "CreatedAtUtc", "TraceParent", "Payload" from "OutboxMessages" where "Id" = '${stored[0].Id}'`);
    expect(outbox).toHaveLength(1);
    expect(outbox[0]).toMatchObject({ EventType: 'disputedesk.dispute.raised.v1', DisputeId: id, DisputeVersion: 1, Status: expect.stringMatching(/^(Pending|Sent)$/) });
    expect(rows<any>(`select 1 from "OutboxMessages" where "DisputeId" = '${id}'`)).toHaveLength(1);

    // One instant on all three rows, and the CloudEvent time is that instant.
    expect(stored[0].OccurredAtUtc).toBe(dispute[0].CreatedAtUtc);
    expect(stored[0].OccurredAtUtc).toBe(outbox[0].CreatedAtUtc);
    const payload = JSON.parse(outbox[0].Payload);
    expect(instant(payload.time)).toBe(instant(stored[0].OccurredAtUtc));
    expect(instant(payload.data.occurredAt)).toBe(instant(stored[0].OccurredAtUtc));
    expect(fractionDigits(payload.time)).toBeLessThanOrEqual(6);
    expect(payload.id).toBe(stored[0].Id);

    // One trace: the stored event's trace ID is the outbox row's traceparent trace ID.
    expect(stored[0].TraceId).toMatch(/^[0-9a-f]{32}$/);
    expect(outbox[0].TraceParent.split('-')[1]).toBe(stored[0].TraceId);

    // No audit row for a successful raise.
    expect(rows(`select 1 from "AuditLogEntries" where "DisputeId" = '${id}'`)).toHaveLength(0);
    const after = totals();
    expect(after.disputes - totalsBeforeRaise.disputes).toBe(1);
    expect(after.stored - totalsBeforeRaise.stored).toBe(1);
    expect(after.outbox - totalsBeforeRaise.outbox).toBe(1);
    expect(after.audit - totalsBeforeRaise.audit).toBe(0);
  });

  test('stored event Data holds only the raise fields, and the outbox payload holds no private field', async () => {
    const id = raisedByA.disputeId;
    const [stored] = rows<any>(`select "Data" from "StoredEvents" where "DisputeId" = '${id}'`);
    expect(Object.keys(stored.Data).sort()).toEqual(
      ['customerId', 'disputedAmount', 'expectedAmount', 'note', 'reason', 'toStatus', 'transaction'].sort());
    // The envelope fields live in columns, never repeated in Data.
    for (const column of ['occurredAtUtc', 'actorType', 'actorId', 'traceId', 'version', 'outcome', 'fromStatus']) {
      expect(stored.Data).not.toHaveProperty(column);
    }
    const [outbox] = rows<any>(`select "Payload" from "OutboxMessages" where "DisputeId" = '${id}'`);
    const payload = JSON.parse(outbox.Payload);
    expect(Object.keys(payload.data).sort()).toEqual(['disputeId', 'disputeVersion', 'occurredAt', 'reason', 'status']);
    expect(payload).toMatchObject({ specversion: '1.0', type: 'disputedesk.dispute.raised.v1', subject: id, data: { disputeId: id, reason: 'unrecognised', status: 'submitted', disputeVersion: 1 } });
    // A stored event is never published as is: nothing private reaches the outbox row's text.
    for (const privateValue of [stored.Data.customerId, raisedByA.transaction.accountRef, raisedByA.transaction.transactionId, raisedByA.transaction.description, 'I did not make this purchase.']) {
      expect(outbox.Payload).not.toContain(privateValue);
    }
  });

  test('the 201 timeline equals what the read model and the stream give', async () => {
    const id = raisedByA.disputeId;
    const [stored] = rows<any>(`select "OccurredAtUtc", "Data" from "StoredEvents" where "DisputeId" = '${id}'`);
    const [dispute] = rows<any>(`select "CreatedAtUtc", "Status", "Note" from "Disputes" where "Id" = '${id}'`);
    const entry = raisedByA.body.timeline[0];
    expect(instant(entry.occurredAt)).toBe(instant(stored.OccurredAtUtc));
    const [outboxRow] = rows<any>(`select "CreatedAtUtc", "Payload" from "OutboxMessages" where "DisputeId" = '${id}'`);
    const cloudEvent = JSON.parse(outboxRow.Payload);
    // Printed as evidence: the seven instants of one raise, as each hop wrote them (synthetic data, no personal data).
    console.log(JSON.stringify({ created201: raisedByA.body.createdAt, timeline201: entry.occurredAt, disputesCreatedAtUtc: dispute.CreatedAtUtc,
      storedOccurredAtUtc: stored.OccurredAtUtc, outboxCreatedAtUtc: outboxRow.CreatedAtUtc, cloudEventTime: cloudEvent.time, cloudEventDataOccurredAt: cloudEvent.data.occurredAt }, null, 1));
    for (const other of [entry.occurredAt, dispute.CreatedAtUtc, outboxRow.CreatedAtUtc, cloudEvent.time, cloudEvent.data.occurredAt]) {
      expect(instant(other)).toBe(instant(raisedByA.body.createdAt));
    }
    expect(fractionDigits(entry.occurredAt)).toBeLessThanOrEqual(6);
    expect(instant(raisedByA.body.createdAt)).toBe(instant(dispute.CreatedAtUtc));
    expect(instant(raisedByA.body.createdAt)).toBe(instant(entry.occurredAt));
    expect(entry.note).toBe(stored.Data.note);
    expect(entry.note).toBe(dispute.Note);
    expect(entry.customerStatus).toBe(stored.Data.toStatus);
    expect(raisedByA.body.customerStatus).toBe('submitted');
    expect(dispute.Status).toBe('Submitted');
  });

  test('the same Idempotency-Key replays the same body byte for byte and writes nothing more', async () => {
    const before = totals();
    const first = await raise(contexts.a.request, raisedByA.key, raisedByA.request);
    const second = await raise(contexts.a.request, raisedByA.key, raisedByA.request);
    expect(first.status()).toBe(201);
    expect(second.status()).toBe(201);
    const original = JSON.stringify(raisedByA.body);
    expect(await first.text()).toBe(await second.text());
    expect(JSON.parse(await first.text()).disputeId).toBe(raisedByA.disputeId);
    expect(JSON.stringify(JSON.parse(await second.text()))).toBe(original);
    expect(totals()).toEqual(before);
    expect(rows(`select 1 from "StoredEvents" where "DisputeId" = '${raisedByA.disputeId}'`)).toHaveLength(1);
  });

  test('another request under the key is 422, and a new key on the same transaction is 409 with the existing dispute', async () => {
    const before = totals();
    const mismatch = await raise(contexts.a.request, raisedByA.key, { ...raisedByA.request, reason: 'duplicateCharge' });
    expect(mismatch.status()).toBe(422);
    expect(expectProblemShape(await mismatch.json(), 'idempotency-mismatch', 422)).toEqual([]);

    const again = await raise(contexts.a.request, unique(), raisedByA.request);
    expect(again.status()).toBe(409);
    const problem = await again.json();
    expect(expectProblemShape(problem, 'already-disputed', 409)).toEqual([]);
    expect(problem).toMatchObject({ disputeId: raisedByA.disputeId, customerStatus: 'submitted' });
    // Story 3.4: the 409 is a refusal on rule, so it is audited (one Rejected row); the 422 mismatch is not; nothing else is written.
    expect(totals()).toEqual({ ...before, audit: before.audit + 1 });
  });

  test('wrong amount without a note: the disputed amount is the difference and Data holds only the raise fields', async () => {
    const transaction = aRows[1];
    const response = await raise(contexts.a.request, unique(), { accountRef: transaction.accountRef, transactionId: transaction.transactionId, reason: 'wrongAmount', expectedAmount: '1.00' });
    expect(response.status()).toBe(201);
    const body = await response.json();
    expect(body.note).toBeNull();
    expect(body.expectedAmount).toEqual({ amount: '1.00', currency: 'ZAR' });
    const [stored] = rows<any>(`select "Version", "EventType", "Data" from "StoredEvents" where "DisputeId" = '${body.disputeId}'`);
    expect(stored).toMatchObject({ Version: 1, EventType: 'DisputeRaised' });
    expect(stored.Data.note).toBeNull();
    expect(stored.Data.expectedAmount).toBe(1);
    const [dispute] = rows<any>(`select "ExpectedAmount", "DisputedAmount", "Outcome" from "Disputes" where "Id" = '${body.disputeId}'`);
    expect(dispute.Outcome).toBeNull();
    expect(Number(dispute.DisputedAmount)).toBeCloseTo(Number(body.transaction.amount.amount) - 1, 2);
    expect(stored.Data.disputedAmount).toBe(Number(dispute.DisputedAmount));
  });
});

test.describe('customer B and the refused requests', () => {
  test('B cannot raise on A’s transaction, reuse A’s key, or read A’s dispute: 404 and nothing leaked', async () => {
    test.skip(!raisedByA, 'needs the raise from the serial block');
    const before = totals();
    const crossed = await raise(contexts.b.request, unique(), raisedByA.request);
    expect(crossed.status()).toBe(404);
    const crossedText = await crossed.text();
    expect(expectProblemShape(JSON.parse(crossedText), 'not-found', 404)).toEqual([]);

    // A's key under B's session finds no stored answer of A's.
    const reusedKey = await raise(contexts.b.request, raisedByA.key, raisedByA.request);
    expect(reusedKey.status()).toBe(404);
    const reusedText = await reusedKey.text();
    for (const text of [crossedText, reusedText]) {
      expect(text).not.toContain(raisedByA.disputeId);
      expect(text).not.toContain('submitted');
      expect(text).not.toContain(raisedByA.transaction.description);
    }

    const read = await contexts.b.request.get(`/api/v1/disputes/${raisedByA.disputeId}`);
    expect(read.status()).toBe(404);
    expect(expectProblemShape(await read.json(), 'not-found', 404)).toEqual([]);

    // B's list shows none of A's disputes, and nothing was written for B.
    const bList = await (await contexts.b.request.get('/api/v1/transactions?limit=50')).json();
    expect(JSON.stringify(bList)).not.toContain(raisedByA.disputeId);
    // Story 3.4: each refused raise (404) is audited as one Rejected row with no dispute ID; no dispute, event or outbox row is written.
    expect(totals()).toEqual({ ...before, audit: before.audit + 2 });
    expect(rows(`select 1 from "AuditLogEntries" where "Action" = 'Raise' and "DisputeId" is null and "CustomerId" is not null and "OccurredAtUtc" > now() - interval '1 minute'`).length).toBeGreaterThanOrEqual(2);
    expect(rows(`select 1 from "StoredEvents" where "DisputeId" = '${raisedByA.disputeId}'`)).toHaveLength(1);
  });

  test('B raises their own: its stream and rows are B’s, and A’s dispute is still one event', async () => {
    const transaction = bRows[0];
    const response = await raise(contexts.b.request, unique(), { accountRef: transaction.accountRef, transactionId: transaction.transactionId, reason: 'duplicateCharge' });
    expect(response.status()).toBe(201);
    const body = await response.json();
    raisedByB = { disputeId: body.disputeId, transaction };
    const owners = rows<{ customer: string; events: number }>(
      `select d."CustomerId" as customer, count(s.*)::int as events from "Disputes" d join "StoredEvents" s on s."DisputeId" = d."Id" where d."Id" in ('${body.disputeId}', '${raisedByA?.disputeId ?? body.disputeId}') group by 1 order by 1`);
    expect(owners.every((owner) => owner.events === 1)).toBe(true);
    if (raisedByA) expect(owners).toHaveLength(2);
  });

  test('refusals write no dispute, stored event or outbox row: no key, no antiforgery, anonymous, unlinked, bad body', async ({ request }) => {
    const before = totals();
    const transaction = aRows[2] ?? aRows[0];
    const body = { accountRef: transaction.accountRef, transactionId: transaction.transactionId, reason: 'unrecognised' };

    const noKey = await raise(contexts.a.request, null, body);
    expect(noKey.status()).toBe(400);
    expect(expectProblemShape(await noKey.json(), 'validation', 400)).toEqual([]);

    const noToken = await contexts.a.request.post('/api/v1/disputes', { headers: { 'Idempotency-Key': `"${unique()}"` }, data: body });
    expect(noToken.status()).toBe(400);

    const anonymous = await request.post('/api/v1/disputes', { headers: { 'Idempotency-Key': `"${unique()}"` }, data: body, maxRedirects: 0 });
    expect(anonymous.status()).toBe(401);
    expect(expectProblemShape(await anonymous.json(), 'unauthorized', 401)).toEqual([]);

    const unlinked = await raise(contexts.c.request, unique(), body);
    expect(unlinked.status()).toBe(403);
    expect(expectProblemShape(await unlinked.json(), 'linking-required', 403)).toEqual([]);

    const wrongAmountWithoutExpected = await raise(contexts.a.request, unique(), { ...body, reason: 'wrongAmount' });
    expect(wrongAmountWithoutExpected.status()).toBe(400);
    const problem = await wrongAmountWithoutExpected.json();
    expect(expectProblemShape(problem, 'validation', 400)).toEqual([]);
    expect(problem.errors).toHaveProperty('expectedAmount');

    expect(totals()).toEqual(before);
  });
});

test.describe('the data model and the logs', () => {
  test('no timeline table; StoredEvents has only its key and the unique version index; the migration history keeps its IDs', async () => {
    const tables = rows<{ name: string }>(`select table_name as name from information_schema.tables where table_schema = 'public' order by 1`).map((table) => table.name);
    expect(tables).toContain('StoredEvents');
    expect(tables.filter((name) => /timeline/i.test(name))).toEqual([]);

    const indexes = rows<{ name: string }>(`select indexname as name from pg_indexes where tablename = 'StoredEvents' order by 1`).map((index) => index.name);
    expect(indexes).toEqual(['PK_StoredEvents', 'UQ_StoredEvents_DisputeId_Version']);

    const foreignKey = rows<{ rule: string }>(
      `select confdeltype::text as rule from pg_constraint where conname = 'FK_StoredEvents_Disputes_DisputeId'`);
    expect(foreignKey).toEqual([{ rule: 'r' }]);

    const columns = rows<{ name: string; type: string }>(
      `select column_name as name, data_type as type from information_schema.columns where table_name = 'StoredEvents' order by ordinal_position`);
    expect(columns).toEqual([
      { name: 'Id', type: 'uuid' }, { name: 'DisputeId', type: 'uuid' }, { name: 'Version', type: 'integer' }, { name: 'EventType', type: 'text' },
      { name: 'OccurredAtUtc', type: 'timestamp with time zone' }, { name: 'ActorType', type: 'text' }, { name: 'ActorId', type: 'text' },
      { name: 'TraceId', type: 'text' }, { name: 'Data', type: 'jsonb' },
    ]);

    const disputeColumns = rows<{ name: string }>(`select column_name as name from information_schema.columns where table_name = 'Disputes'`).map((column) => column.name);
    expect(disputeColumns).toContain('Outcome');
    expect(disputeColumns.filter((name) => /rowversion|xmin|concurrency/i.test(name))).toEqual([]);

    const migrations = rows<{ id: string }>(`select "MigrationId" as id from "__EFMigrationsHistory" order by 1`).map((migration) => migration.id);
    expect(migrations).toContain('20261028084134_AddDisputes');
    expect(migrations).toContain('20261028150231_AddIdempotencyKeys');
    expect(migrations.filter((id) => /timeline/i.test(id))).toEqual([]);
  });

  test('the API and worker logs hold no customer ID or account reference', async () => {
    for (const container of ['disputedesk-api-1', 'disputedesk-worker-1']) {
      const logs = execFileSync('docker', ['logs', container], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024 });
      expect(logs.match(/CUS-\d+|ACC-[A-Z]-\d+/g) ?? [], `${container} log`).toEqual([]);
    }
  });
});

test.describe('browser', () => {
  test('the raised transaction shows as already disputed on the transactions screen, with the dispute’s ID in the page’s own request', async ({ browser }) => {
    test.skip(!raisedByB, 'needs the raise from the serial block');
    const context = await signIn(browser, 'customer-b');
    const page: Page = await context.newPage();
    const consoleErrors: string[] = [];
    page.on('console', (message) => message.type() === 'error' && consoleErrors.push(message.text()));
    const listing = page.waitForResponse((response) => response.url().includes('/api/v1/transactions') && response.status() === 200);
    await page.goto('/transactions');
    const body = await (await listing).json();
    const row = body.items.find((item: any) => item.transactionId === raisedByB!.transaction.transactionId);
    expect(row).toMatchObject({ disputable: false, notDisputableReason: 'alreadyDisputed', disputeId: raisedByB!.disputeId, customerStatus: 'submitted' });
    await expect(page.locator('li').filter({ hasText: raisedByB!.transaction.description }).first()).toBeVisible();
    // The page offers no way to raise yet (story 3.6): no dispute control is on it.
    await expect(page.getByRole('button', { name: /dispute/i })).toHaveCount(0);
    // Console noise other than the known Radix style notice is a failure.
    expect(consoleErrors.filter((text) => !/Applying inline style violates/.test(text))).toEqual([]);
    await context.close();
  });
});
