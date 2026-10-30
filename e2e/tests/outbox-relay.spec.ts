import { execFileSync } from 'node:child_process';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { fixtureLogin, ownDisputable, queryDatabase, topicRecords } from './support';

// Story 4.1: the Worker's outbox relay publishes a raised dispute's event to disputedesk.disputes.
// Four hops per raise: the 201 the browser context gets, the Disputes/StoredEvents/OutboxMessages rows (read-only role
// qa_readonly), the record on the topic (rpk in the redpanda container) and the trace in the Aspire dashboard.
// The raise is sent at network level from a context signed in through Zitadel (there is no raise screen until story 3.6).
// A transaction is disputed once: the spec raises on its reserved transactions (support.ts).

type Row = { transactionId: string; accountRef: string; description: string; disputable: boolean };

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const dashboard = process.env.ASPIRE_ORIGIN ?? 'http://localhost:18888';
let customerA: BrowserContext;
let disputable: Row[];

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

async function raise(api: APIRequestContext, transaction: Row, note: string) {
  const token = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const response = await api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"qa41-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: { accountRef: transaction.accountRef, transactionId: transaction.transactionId, reason: 'unrecognised', note },
    maxRedirects: 0,
  });
  expect(response.status()).toBe(201);
  return (await response.json()).disputeId as string;
}

function rows<T>(sql: string): T[] {
  return JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
}

const outboxOf = (disputeId: string) =>
  rows<any>(`select "Id", "Status", "Attempts", "SentAtUtc", "LastError", "NextAttemptAtUtc", "TraceParent", "TraceState", "Payload", "DisputeVersion" from "OutboxMessages" where "DisputeId" = '${disputeId}'`);

async function untilSent(disputeId: string, seconds: number) {
  await expect.poll(() => outboxOf(disputeId)[0]?.Status, { timeout: seconds * 1000, intervals: [1000] }).toBe('Sent');
}

const docker = (...args: string[]) => execFileSync('docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).trim();
const workerHealth = () => docker('inspect', 'disputedesk-worker-1', '--format', '{{.State.Health.Status}}');
const recordsFor = (disputeId: string) => topicRecords().filter((record) => record.key === disputeId);

test.beforeAll(async ({ browser }) => {
  customerA = await signIn(browser, 'customer-a');
  const response = await customerA.request.get('/api/v1/transactions?limit=50');
  disputable = ownDisputable((await response.json()).items as Row[], 'outbox-relay', 'a');
  expect(disputable.length, 'two of the spec\'s reserved transactions are disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(2);
});

test.afterAll(async () => {
  await customerA.close();
});

test.describe.serial('a raised dispute reaches the topic', () => {
  const note = 'QA 4.1 private note marker';
  let transaction: Row;
  let disputeId: string;

  test('the raise leaves a Pending outbox row that the relay marks Sent', async () => {
    transaction = disputable[0];
    disputeId = await raise(customerA.request, transaction, note);
    const [stored] = rows<any>(`select "Id", "TraceId" from "StoredEvents" where "DisputeId" = '${disputeId}'`);
    const [outbox] = outboxOf(disputeId);
    expect(outbox.Id).toBe(stored.Id);

    await untilSent(disputeId, 30);
    const [sent] = outboxOf(disputeId);
    expect(sent).toMatchObject({ Id: stored.Id, Status: 'Sent', Attempts: 0, LastError: null, DisputeVersion: 1 });
    expect(sent.SentAtUtc).not.toBeNull();
    // The relay changes only its own columns: the identity and the payload are as the raise stored them.
    expect(sent.Payload).toBe(outbox.Payload);
    expect(sent.TraceParent).toBe(outbox.TraceParent);
    expect(sent.TraceParent.split('-')[1]).toBe(stored.TraceId);
  });

  test('exactly one record, keyed by the dispute ID, value byte for byte the stored payload', async () => {
    const [outbox] = outboxOf(disputeId);
    const records = recordsFor(disputeId);
    expect(records).toHaveLength(1);
    const [record] = records;
    expect(record.value).toBe(outbox.Payload);
    const event = JSON.parse(record.value);
    expect(event).toMatchObject({ specversion: '1.0', id: outbox.Id, type: 'disputedesk.dispute.raised.v1', subject: disputeId });
    expect(event.data).toMatchObject({ disputeId, disputeVersion: 1 });
  });

  test('headers are exactly content-type and traceparent (tracestate only when stored), and the trace is the raise trace', async () => {
    const [outbox] = outboxOf(disputeId);
    const [record] = recordsFor(disputeId);
    const names = record.headers.map((header) => header.key).sort();
    expect(names).toEqual(outbox.TraceState ? ['content-type', 'traceparent', 'tracestate'] : ['content-type', 'traceparent']);
    const header = (name: string) => record.headers.find((entry) => entry.key === name)?.value;
    expect(header('content-type')).toBe('application/cloudevents+json');
    expect(header('traceparent')).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/);
    // The producer span is a child of the raise: same trace, a new span.
    expect(header('traceparent')!.split('-')[1]).toBe(outbox.TraceParent.split('-')[1]);
    expect(header('traceparent')!.split('-')[2]).not.toBe(outbox.TraceParent.split('-')[2]);
  });

  test('no customer ID, account, transaction, amount, note or actor on the topic, in the payload or the Worker log', async () => {
    const [record] = recordsFor(disputeId);
    const [stored] = rows<any>(`select "ActorId", "Data" from "StoredEvents" where "DisputeId" = '${disputeId}'`);
    const privateValues = [
      stored.Data.customerId, stored.ActorId, transaction.accountRef, transaction.transactionId, transaction.description, note,
      String(stored.Data.expectedAmount), String(stored.Data.disputedAmount),
    ].filter((value) => value && value !== 'null');
    expect(privateValues.length).toBeGreaterThanOrEqual(6);
    const everything = JSON.stringify(record);
    for (const value of privateValues) expect(everything, `a private value (${value.length} chars)`).not.toContain(value);
    expect(Object.keys(JSON.parse(record.value).data).sort()).toEqual(['disputeId', 'disputeVersion', 'occurredAt', 'reason', 'status']);
    const log = docker('logs', 'disputedesk-worker-1');
    for (const value of [stored.Data.customerId, transaction.accountRef, transaction.transactionId, note]) expect(log).not.toContain(value);
  });

  test('every record on the topic follows the contract: one per outbox row, keyed by dispute, per-dispute versions in order', async () => {
    const records = topicRecords();
    const outbox = rows<any>(`select "Id", "DisputeId", "DisputeVersion", "Status" from "OutboxMessages" where "Status" = 'Sent' order by "DisputeId", "DisputeVersion"`);
    expect(records.map((record) => JSON.parse(record.value).id).sort()).toEqual(outbox.map((row) => row.Id).sort());
    for (const record of records) {
      expect(record.key).toBe(JSON.parse(record.value).subject);
      expect(record.headers.map((header) => header.key).sort()).toEqual(expect.arrayContaining(['content-type', 'traceparent']));
      expect(record.headers.map((header) => header.key)).not.toContain('baggage');
    }
    // One partition per key, and offsets rise with the version within a dispute.
    const byDispute = new Map<string, { partition: number; offset: number; version: number }[]>();
    for (const record of records) {
      const list = byDispute.get(record.key) ?? [];
      list.push({ partition: record.partition, offset: record.offset, version: JSON.parse(record.value).data.disputeVersion });
      byDispute.set(record.key, list);
    }
    for (const list of byDispute.values()) {
      expect(new Set(list.map((entry) => entry.partition)).size).toBe(1);
      const byOffset = [...list].sort((a, b) => a.offset - b.offset).map((entry) => entry.version);
      expect(byOffset).toEqual([...byOffset].sort((a, b) => a - b));
    }
  });

  test('the Worker runs as a non-root user and its relay heartbeat health check is healthy', async () => {
    expect(docker('exec', 'disputedesk-worker-1', '/usr/local/bin/busybox', 'id', '-u')).not.toBe('0');
    expect(workerHealth()).toBe('healthy');
    const busybox = (...args: string[]) => Number(docker('exec', 'disputedesk-worker-1', '/usr/local/bin/busybox', ...args));
    const age = busybox('date', '+%s') - busybox('stat', '-c', '%Y', '/tmp/heartbeat-relay');
    expect(age).toBeLessThanOrEqual(15);
  });

  test('one trace from the raise to the Worker producer span in the Aspire dashboard', async ({ page }) => {
    const [outbox] = outboxOf(disputeId);
    const traceId = outbox.TraceParent.split('-')[1];
    await page.goto(`${dashboard}/traces/detail/${traceId}`);
    // The dashboard names the resource disputedesk-worker-<instance> once the Worker has run as two instances (a restart).
    // The dashboard shows the Producer span `send disputedesk.disputes` as "MSG kafka disputedesk.disputes".
    await expect(page.getByRole('row', { name: /disputedesk-worker[^:]*: MSG kafka disputedesk\.disputes/ })).toBeVisible({ timeout: 45_000 });
    const text = await page.locator('body').innerText();
    expect(text).toContain('disputedesk-api');
    expect(text).toContain('disputedesk-worker');
    expect(text).toMatch(/POST \/api\/v1\/disputes/);
  });
});

test.describe.serial('the broker is stopped', () => {
  test('the row stays Pending with Attempts unchanged and the Worker stays healthy; restarting the broker publishes it', async () => {
    test.setTimeout(240_000);
    const transaction = disputable[1];
    docker('stop', 'disputedesk-redpanda-1');
    let disputeId = '';
    try {
      disputeId = await raise(customerA.request, transaction, 'QA 4.1 outage marker');
      // Longer than the 30 s health threshold, so a relay that stopped touching its heartbeat would turn unhealthy.
      for (let second = 0; second < 40; second += 4) {
        await new Promise((resolve) => setTimeout(resolve, 4000));
        const [row] = outboxOf(disputeId);
        expect(row, `after ${second + 4} s`).toMatchObject({ Status: 'Pending', Attempts: 0, SentAtUtc: null, LastError: null });
      }
      expect(workerHealth()).toBe('healthy');
      const log = docker('logs', '--since', '60s', 'disputedesk-worker-1');
      expect(log).toMatch(/warn: DisputeDesk\.Infrastructure\.Outbox\.OutboxRelay/);
      expect(log).not.toContain(disputeId);
      expect(log).not.toMatch(/Password=/i);
    } finally {
      docker('start', 'disputedesk-redpanda-1');
    }
    await untilSent(disputeId, 120);
    const [row] = outboxOf(disputeId);
    expect(row).toMatchObject({ Status: 'Sent', Attempts: 0, LastError: null });
    const records = recordsFor(disputeId);
    expect(records).toHaveLength(1);
    expect(JSON.parse(records[0].value).id).toBe(row.Id);
    expect(workerHealth()).toBe('healthy');
  });
});
