import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { fixtureLogin, ownDisputable, queryDatabase, topicRecords } from './support';

// Story 4.2: the Worker's outbox relay rides out a broker outage and a shutdown.
// Four hops: the 201 the browser context gets, the OutboxMessages rows (read-only role qa_readonly), the records on the
// topic (rpk in the redpanda container) and the Worker's log and the Aspire traces (no customer data on either).
// A transaction is disputed once: the spec raises on its reserved transactions (support.ts).

type Row = { transactionId: string; accountRef: string; description: string; disputable: boolean };
type Raised = { disputeId: string; transaction: Row; note: string };

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const dashboard = process.env.ASPIRE_ORIGIN ?? 'http://localhost:18888';
const repositoryRoot = fileURLToPath(new URL('../..', import.meta.url));
const outageSeconds = 30;
const graceSeconds = 35;
let customerA: BrowserContext;
let customerB: BrowserContext;
let disputableA: Row[];
let disputableB: Row[];

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

async function raise(api: APIRequestContext, transaction: Row, note: string): Promise<Raised> {
  const token = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const response = await api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"qa42-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: { accountRef: transaction.accountRef, transactionId: transaction.transactionId, reason: 'unrecognised', note },
    maxRedirects: 0,
  });
  expect(response.status()).toBe(201);
  return { disputeId: (await response.json()).disputeId as string, transaction, note };
}

function rows<T>(sql: string): T[] {
  return JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
}

const outboxOf = (disputeId: string) =>
  rows<any>(`select "Id", "Status", "Attempts", "SentAtUtc", "LastError", "NextAttemptAtUtc", "TraceParent", "Payload", "DisputeVersion" from "OutboxMessages" where "DisputeId" = '${disputeId}' order by "DisputeVersion"`);

const composeEnvironment = {
  ...process.env,
  DISPUTEDESK_OTLP_GRPC_PORT: process.env.DISPUTEDESK_OTLP_GRPC_PORT ?? '14317',
  DISPUTEDESK_OTLP_HTTP_PORT: process.env.DISPUTEDESK_OTLP_HTTP_PORT ?? '14318',
};
const docker = (...args: string[]) => execFileSync('docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).trim();
const compose = (...args: string[]) =>
  execFileSync('docker', ['compose', ...args], { encoding: 'utf8', cwd: repositoryRoot, env: composeEnvironment, stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const workerHealth = () => docker('inspect', 'disputedesk-worker-1', '--format', '{{.State.Health.Status}}');
const recordsFor = (disputeId: string) => topicRecords().filter((record) => record.key === disputeId);
/** AD-17 is at-least-once: at least one record per row, and every copy carries the row's CloudEvent id (the outbox ID). */
function expectRecordsOf(disputeId: string, outboxId: string, description: string) {
  const records = recordsFor(disputeId);
  expect(records.length, `${description}: at least one record`).toBeGreaterThanOrEqual(1);
  for (const record of records) {
    expect(record.key).toBe(disputeId);
    expect(JSON.parse(record.value).id).toBe(outboxId);
  }
  return records;
}
const sleep = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

async function untilSent(disputeId: string, seconds: number) {
  await expect.poll(() => outboxOf(disputeId)[0]?.Status, { timeout: seconds * 1000, intervals: [1000] }).toBe('Sent');
}

/** The Worker's log lines between two instants, grouped: each console-logger entry is a header line (level and category) then its message. */
function workerEntries(since: string, until: string) {
  const lines = docker('logs', '--since', since, '--until', until, 'disputedesk-worker-1').split('\n');
  const entries: { level: string; category: string; message: string }[] = [];
  for (let index = 0; index < lines.length; index++) {
    const header = lines[index].match(/^(\w+): ([\w.]+)\[\d+\]/);
    if (header) entries.push({ level: header[1], category: header[2], message: (lines[index + 1] ?? '').trim() });
  }
  return entries;
}

function markersOf(raised: Raised[]): string[] {
  const markers = raised.flatMap(({ disputeId, transaction, note }) => {
    const [stored] = rows<any>(`select "ActorId", "Data" from "StoredEvents" where "DisputeId" = '${disputeId}'`);
    return [stored.Data.customerId, stored.ActorId, transaction.accountRef, transaction.transactionId, transaction.description, note];
  });
  return markers.filter((value) => value && value !== 'null');
}

test.beforeAll(async ({ browser }) => {
  customerA = await signIn(browser, 'customer-a');
  customerB = await signIn(browser, 'customer-b');
  const take = async (context: BrowserContext, customer: 'a' | 'b') =>
    ownDisputable((await (await context.request.get('/api/v1/transactions?limit=50')).json()).items as Row[], 'outbox-relay-failure-paths', customer);
  disputableA = await take(customerA, 'a');
  disputableB = await take(customerB, 'b');
  expect(disputableA.length, 'four of the spec\'s reserved customer A transactions are disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(4);
  expect(disputableB.length, 'two of the spec\'s reserved customer B transactions are disputable (reservedTransactions in support.ts)').toBeGreaterThanOrEqual(2);
});

test.afterAll(async () => {
  try {
    docker('start', 'disputedesk-redpanda-1');
    compose('start', 'worker');
  } catch {
    // Already running.
  }
  await customerA.close();
  await customerB.close();
});

test.describe.serial('a 30 second broker outage', () => {
  test.setTimeout(300_000);
  const raised: Raised[] = [];
  let outageStart = '';
  let outageEnd = '';

  test('raises succeed, rows stay Pending with Attempts 0 and no LastError, the Worker stays healthy', async () => {
    docker('stop', 'disputedesk-redpanda-1');
    const startedAt = Date.now();
    outageStart = new Date(startedAt).toISOString();
    raised.push(await raise(customerA.request, disputableA[0], 'QA 4.2 outage note marker one'));
    raised.push(await raise(customerA.request, disputableA[1], 'QA 4.2 outage note marker two'));
    raised.push(await raise(customerB.request, disputableB[0], 'QA 4.2 outage note marker three'));
    const healthSamples: string[] = [];
    while (Date.now() - startedAt < outageSeconds * 1000) {
      await sleep(5000);
      healthSamples.push(workerHealth());
      for (const { disputeId } of raised) {
        expect(outboxOf(disputeId)[0], `row of ${disputeId.slice(0, 8)} during the outage`).toMatchObject({ Status: 'Pending', Attempts: 0, SentAtUtc: null, LastError: null });
      }
    }
    outageEnd = new Date().toISOString();
    expect(healthSamples.every((state) => state === 'healthy'), `health samples ${healthSamples.join(',')}`).toBe(true);
    console.log(`QA42 outage health samples: ${healthSamples.join(',')}`);
  });

  test('the warnings of the outage hold only codes; their number is reported', async () => {
    const entries = workerEntries(outageStart, outageEnd);
    const warnings = entries.filter((entry) => entry.level === 'warn');
    const tally = new Map<string, number>();
    for (const entry of warnings) tally.set(entry.message, (tally.get(entry.message) ?? 0) + 1);
    console.log(`QA42 outage window ${outageStart} to ${outageEnd}: ${entries.length} log entries, ${warnings.length} warnings`);
    for (const [message, count] of tally) console.log(`QA42 warning x${count}: ${message}`);
    console.log(`QA42 non-warn levels: ${JSON.stringify([...new Set(entries.filter((entry) => entry.level !== 'warn').map((entry) => `${entry.level}:${entry.message}`))])}`);
    expect(warnings.length).toBeGreaterThan(0);
    const log = docker('logs', '--since', outageStart, '--until', outageEnd, 'disputedesk-worker-1');
    for (const marker of markersOf(raised)) expect(log, 'a private value in the Worker log').not.toContain(marker);
    for (const { disputeId } of raised) expect(log).not.toContain(disputeId);
    expect(log).not.toMatch(/Password=|Host=|redpanda:9092|Exception/i);
    for (const message of tally.keys()) {
      // Only a dependency name and an error code, or a facility, or a fixed sentence.
      expect(message).toMatch(/^(Outbox relay cannot reach \w+ \(\w+\)|Outbox relay could not send an event \(\w+\); it stays Pending and is retried|Kafka client log line \(\w+\))$/);
    }
  });

  test('after the broker returns every row is Sent, keyed by dispute, with the outbox ID as the CloudEvent id', async () => {
    docker('start', 'disputedesk-redpanda-1');
    for (const { disputeId } of raised) await untilSent(disputeId, 120);
    for (const { disputeId } of raised) {
      const outbox = outboxOf(disputeId);
      expect(outbox).toHaveLength(1);
      expect(outbox[0]).toMatchObject({ Status: 'Sent', Attempts: 0, LastError: null, DisputeVersion: 1 });
      expect(outbox[0].SentAtUtc).not.toBeNull();
      const records = expectRecordsOf(disputeId, outbox[0].Id, 'after the outage');
      expect(records[0].key).toBe(disputeId);
      expect(records[0].value).toBe(outbox[0].Payload);
      const event = JSON.parse(records[0].value);
      expect(event.id).toBe(outbox[0].Id);
      expect(event.data.disputeVersion).toBe(1);
      const names = records[0].headers.map((header) => header.key);
      expect(names).toEqual(expect.arrayContaining(['content-type', 'traceparent']));
      expect(names).not.toContain('baggage');
    }
    expect(workerHealth()).toBe('healthy');
  });

  test('the Worker log and the Aspire traces of the outage hold no customer data', async ({ page }) => {
    const markers = markersOf(raised);
    const log = docker('logs', '--since', outageStart, 'disputedesk-worker-1');
    for (const marker of markers) expect(log, 'a private value in the Worker log').not.toContain(marker);
    // The dashboard shows each raise's trace with the Worker's producer span, where error.type sits on a failed send.
    let errorSpanTraces = 0;
    for (const { disputeId } of raised) {
      const traceId = outboxOf(disputeId)[0].TraceParent.split('-')[1];
      await page.goto(`${dashboard}/traces/detail/${traceId}`);
      // The dashboard names the resource disputedesk-worker-<instance> once the Worker has run as two instances (a restart).
      // A raise during the outage has failed sends (Status = Error) and the final successful one.
      const producerRows = page.getByRole('row', { name: /disputedesk-worker[^:]*: MSG kafka disputedesk\.disputes/ });
      // The Worker exports in batches; reload the trace until its producer spans arrive.
      await expect(async () => {
        await page.reload();
        await expect(producerRows.last(), `producer span of trace ${traceId.slice(0, 8)}`).toBeVisible({ timeout: 4000 });
      }).toPass({ timeout: 60_000, intervals: [2000] });
      // A transient failure ends the batch, so only rows attempted during the outage have failed sends (Status = Error); the rest first send after recovery.
      errorSpanTraces += await page.getByRole('row', { name: /disputedesk-worker[^:]*: MSG kafka disputedesk\.disputes Status = Error/ }).count() > 0 ? 1 : 0;
      const text = await page.locator('body').innerText();
      for (const marker of markers) expect(text, 'a private value in the trace').not.toContain(marker);
    }
    expect(errorSpanTraces, 'at least one row attempted during the outage has an Error producer span').toBeGreaterThanOrEqual(1);
    // Every span the Worker sent and every Worker log record in the dashboard, searched as page text.
    for (const path of ['/traces', '/structuredlogs']) {
      await page.goto(`${dashboard}${path}`);
      await page.waitForLoadState('networkidle');
      const text = await page.locator('body').innerText();
      for (const marker of markers) expect(text, `a private value on ${path}`).not.toContain(marker);
    }
  });
});

test.describe.serial('the Worker is stopped while a send is in flight', () => {
  test.setTimeout(300_000);
  const raised: Raised[] = [];

  test('stop finishes inside the grace period with exit 0 and leaves no half-written row', async () => {
    // The broker is down, so the relay has a send in flight (up to its 10 s delivery timeout) when SIGTERM arrives.
    docker('stop', 'disputedesk-redpanda-1');
    raised.push(await raise(customerA.request, disputableA[2], 'QA 4.2 shutdown note marker one'));
    raised.push(await raise(customerB.request, disputableB[1], 'QA 4.2 shutdown note marker two'));
    await sleep(3000);
    const startedAt = Date.now();
    compose('stop', 'worker');
    const seconds = (Date.now() - startedAt) / 1000;
    const exitCode = docker('inspect', 'disputedesk-worker-1', '--format', '{{.State.ExitCode}}');
    console.log(`QA42 worker stop took ${seconds.toFixed(1)} s, exit code ${exitCode}`);
    expect(seconds).toBeLessThan(graceSeconds);
    expect(exitCode).toBe('0');
    for (const { disputeId } of raised) {
      const [row] = outboxOf(disputeId);
      // Never Failed, never Sent without its record: with the broker down the in-flight row is Pending, untouched.
      expect(row, 'row after stop').toMatchObject({ Status: 'Pending', Attempts: 0, SentAtUtc: null, LastError: null });
    }
    const log = docker('logs', '--tail', '40', 'disputedesk-worker-1');
    // librdkafka's FAIL facility arrives at error level (Kafka client log line); nothing else is error or critical.
    const severe = workerEntries('5m', '0s').filter((entry) => entry.level === 'fail' || entry.level === 'crit');
    expect(severe.every((entry) => entry.message === 'Kafka client log line (FAIL)'), JSON.stringify(severe.map((entry) => entry.message))).toBe(true);
    console.log(`QA42 error-level lines in last 5 min: ${severe.length}`);
    expect(log).not.toMatch(/^crit:/m);
    console.log(`QA42 worker last log level counts: ${JSON.stringify(workerEntries('5m', '0s').slice(-10).map((entry) => entry.level))}`);
  });

  test('restarting the Worker and the broker resumes: every row Sent with its record', async () => {
    docker('start', 'disputedesk-redpanda-1');
    compose('start', 'worker');
    for (const { disputeId } of raised) await untilSent(disputeId, 150);
    for (const { disputeId } of raised) {
      const [row] = outboxOf(disputeId);
      expect(row).toMatchObject({ Status: 'Sent', Attempts: 0, LastError: null });
      expectRecordsOf(disputeId, row.Id, 'after the restart');
    }
    await expect.poll(workerHealth, { timeout: 90_000, intervals: [2000] }).toBe('healthy');
    const log = docker('logs', 'disputedesk-worker-1');
    for (const marker of markersOf(raised)) expect(log, 'a private value in the Worker log').not.toContain(marker);
  });
});

test.describe.serial('the Worker is stopped with the broker up', () => {
  test.setTimeout(180_000);

  test('a stop right after a raise exits 0 within the grace period; every row is Sent with its record or Pending untouched', async () => {
    const raised = [await raise(customerA.request, disputableA[3], 'QA 4.2 live stop note marker')];
    const startedAt = Date.now();
    compose('stop', 'worker');
    const seconds = (Date.now() - startedAt) / 1000;
    console.log(`QA42 live stop took ${seconds.toFixed(1)} s, exit code ${docker('inspect', 'disputedesk-worker-1', '--format', '{{.State.ExitCode}}')}`);
    expect(seconds).toBeLessThan(graceSeconds);
    expect(docker('inspect', 'disputedesk-worker-1', '--format', '{{.State.ExitCode}}')).toBe('0');
    const [row] = outboxOf(raised[0].disputeId);
    if (row.Status === 'Sent') {
      expectRecordsOf(raised[0].disputeId, row.Id, 'Sent only with its record on the topic');
    } else {
      expect(row).toMatchObject({ Status: 'Pending', Attempts: 0, LastError: null, SentAtUtc: null });
    }
    compose('start', 'worker');
    await untilSent(raised[0].disputeId, 90);
    expectRecordsOf(raised[0].disputeId, row.Id, 'after the restart');
  });
});
