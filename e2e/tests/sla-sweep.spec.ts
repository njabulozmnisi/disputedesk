import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase, topicRecords } from './support';

// Story 5.3: the Worker's SLA sweep on the real stack. The seeded SLA demo dispute (customer A, due 5 minutes after the
// seeding instant) is escalated by the sweep as `automation` / `worker:sla-sweep`; the sweep first records one
// `SlaWarningRaised` (the dispute is inside the 48-hour lead time from the start), which is never published and never
// shown to the customer. Four hops: the SPA badge, the API response, the database (qa_readonly) and the topic.
// Needs a fresh stack with the committed fixture (the demo dispute is due at seed time + 5 minutes); the spec waits for it.
// The spec reads only. The one write is the operations escalate that is refused on the already escalated demo dispute.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const root = new URL('../..', import.meta.url).pathname;
const fixtures = JSON.parse(readFileSync(fileURLToPath(new URL('../../fixtures/local.json', import.meta.url)), 'utf8'));
const demo = fixtures.disputes.find((entry: { slaDemo?: boolean }) => entry.slaDemo) as { accountRef: string; transactionId: string };
const worker = 'disputedesk-worker-1';

type Json = Record<string, any>;

function rows<T = Json>(sql: string): T[] {
  return JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
}
const docker = (...args: string[]) => execFileSync('docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const workerHealth = () => docker('inspect', worker, '--format', '{{.State.Health.Status}}');
const sleep = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

const eventsOf = (id: string) =>
  rows(`select "Id", "Version", "EventType", "ActorType", "ActorId", to_char("OccurredAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') as "At", "Data" from "StoredEvents" where "DisputeId" = '${id}' order by "Version"`);
const outboxOf = (id: string) => rows(`select "Id", "EventType", "DisputeVersion", "Payload", "TraceParent" from "OutboxMessages" where "DisputeId" = '${id}' order by "DisputeVersion"`);

let disputeId: string;
let dueAt: string;
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

test.beforeAll(async ({ browser }) => {
  test.setTimeout(600_000);
  const [row] = rows<{ Id: string; Due: string }>(
    `select "Id", to_char("SlaDueAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') as "Due" from "Disputes" where "AccountRef" = '${demo.accountRef}' and "TransactionId" = '${demo.transactionId}'`);
  expect(row, 'the seeded SLA demo dispute').toBeDefined();
  disputeId = row.Id;
  dueAt = row.Due;
  customerA = await signIn(browser, 'customer-a');
  customerB = await signIn(browser, 'customer-b');
  // Waits for the sweep: up to the due time plus two 30-second loops.
  const deadline = Date.now() + 8 * 60_000;
  while (Date.now() < deadline) {
    if (rows(`select 1 from "Disputes" where "Id" = '${disputeId}' and "Status" = 'Escalated'`).length > 0) return;
    await sleep(3000);
  }
});

test('the sweep escalated the demo dispute within one loop of its due time, once, as automation', async () => {
  const events = eventsOf(disputeId);
  expect(events.map((event) => event.EventType)).toEqual(['DisputeRaised', 'ReviewStarted', 'SlaWarningRaised', 'DisputeEscalated']);
  expect(events.map((event) => event.Version)).toEqual([1, 2, 3, 4]);
  const escalated = events[3];
  expect(escalated).toMatchObject({ ActorType: 'Automation', ActorId: 'worker:sla-sweep' });
  expect(escalated.Data).toEqual({ fromStatus: 'underReview', toStatus: 'escalated' });
  // Not before the SLA is due (FR-26), and within a 30-second loop of it (plus the batch's own time).
  const lateBySeconds = (Date.parse(`${escalated.At}Z`) - Date.parse(`${dueAt}Z`)) / 1000;
  expect(lateBySeconds).toBeGreaterThanOrEqual(0);
  expect(lateBySeconds).toBeLessThanOrEqual(35);
  const [row] = rows(`select "Status", "Version", "SlaWarningRaisedAtUtc" is not null as "Warned" from "Disputes" where "Id" = '${disputeId}'`);
  expect(row).toEqual({ Status: 'Escalated', Version: 4, Warned: true });
  // No audit row for an SLA escalation or warning (NFR-SEC-10).
  expect(rows(`select 1 from "AuditLogEntries" where "DisputeId" = '${disputeId}' and "ActorType" = 'Automation'`)).toHaveLength(0);
});

test('the warning was recorded once, before the deadline, by the sweep, and never published', async () => {
  const events = eventsOf(disputeId);
  const warnings = events.filter((event) => event.EventType === 'SlaWarningRaised');
  expect(warnings).toHaveLength(1);
  expect(warnings[0]).toMatchObject({ ActorType: 'Automation', ActorId: 'worker:sla-sweep', Version: 3, Data: {} });
  expect(Date.parse(`${warnings[0].At}Z`)).toBeLessThan(Date.parse(`${dueAt}Z`));
  // Stored once on the Disputes row too, at the event's instant.
  const [warned] = rows(`select to_char("SlaWarningRaisedAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') as "At" from "Disputes" where "Id" = '${disputeId}'`);
  expect(warned.At).toBe(warnings[0].At);
  // No outbox row, so no record on the topic.
  const outbox = outboxOf(disputeId);
  expect(outbox.map((message) => message.Id)).not.toContain(warnings[0].Id);
  expect(outbox.filter((message) => message.EventType.includes('sla'))).toHaveLength(0);
  expect(topicRecords().filter((record) => record.key === disputeId).map((record) => JSON.parse(record.value).id)).not.toContain(warnings[0].Id);
});

test('one status-changed event with actor type automation and no actor identity: outbox row and topic record agree', async () => {
  const events = eventsOf(disputeId);
  const escalated = events[3];
  const rowsOfEscalation = outboxOf(disputeId).filter((message) => message.Id === escalated.Id);
  expect(rowsOfEscalation).toHaveLength(1);
  const [message] = rowsOfEscalation;
  expect(message).toMatchObject({ EventType: 'disputedesk.dispute.status-changed.v1', DisputeVersion: 4 });
  expect(message.TraceParent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/);
  const published = JSON.parse(message.Payload);
  expect(published.data).toMatchObject({ disputeId, fromStatus: 'underReview', toStatus: 'escalated', action: 'escalate', actorType: 'automation', disputeVersion: 4 });
  expect(JSON.stringify(published)).not.toMatch(/worker:sla-sweep|actorId|"sub"/);

  const onTopic = topicRecords().filter((record) => record.key === disputeId && JSON.parse(record.value).data.action === 'escalate');
  expect(onTopic).toHaveLength(1);
  const value = JSON.parse(onTopic[0].value);
  expect(value.id).toBe(escalated.Id);
  expect(value.data).toMatchObject({ actorType: 'automation', fromStatus: 'underReview', toStatus: 'escalated' });
  expect(onTopic[0].value).not.toMatch(/worker:sla-sweep|actorId|"sub"/);
  // The producer span is a child of the batch span: same trace ID, its own span ID (W3C Trace Context).
  const topicTrace = onTopic[0].headers.find((header) => header.key === 'traceparent')?.value ?? '';
  expect(topicTrace.split('-')[1]).toBe(message.TraceParent.split('-')[1]);
});

test('the customer sees one Under review (prioritised) entry from the bank, never the warning; another customer gets 404', async () => {
  const response = await customerA.request.get(`/api/v1/disputes/${disputeId}`);
  expect(response.status()).toBe(200);
  const text = await response.text();
  const body = JSON.parse(text) as Json;
  expect(body.customerStatus).toBe('underReviewPrioritised');
  const prioritised = body.timeline.filter((entry: Json) => entry.customerStatus === 'underReviewPrioritised');
  expect(prioritised).toHaveLength(1);
  expect(prioritised[0]).toMatchObject({ action: 'escalate', actor: 'theBank' });
  expect(body.timeline).toHaveLength(3); // raise, start review, escalate: the warning is not an entry
  expect(text).not.toMatch(/worker:sla-sweep|automation|slaWarning|warning|actorId/i);

  const other = await customerB.request.get(`/api/v1/disputes/${disputeId}`);
  expect(other.status()).toBe(404);
  expect(expectProblemShape(await other.json(), 'not-found', 404)).toEqual([]);
});

test('the SPA detail screen shows Under review (prioritised) and exactly one escalation entry on the demo dispute', async () => {
  // The history badge for the seeded disputes is checked in seeded-disputes.spec.ts (history API and SPA).
  const page = await customerA.newPage();
  await page.goto(`/disputes/${disputeId}`);
  await expect(page.getByText('Under review (prioritised)').first()).toBeVisible({ timeout: 30_000 });
  const entries = page.locator('ol li');
  await expect(entries).toHaveCount(3);
  expect(await page.locator('body').innerText()).not.toMatch(/warning|automation|worker:/i);
  await page.close();
});

test('operations cannot escalate it again: 409 action-not-allowed and no second DisputeEscalated', async () => {
  const token = execFileSync('scripts/operations-token.sh', { encoding: 'utf8', cwd: root }).trim();
  const ops: APIRequestContext = await playwrightRequest.newContext({ baseURL: origin });
  const before = eventsOf(disputeId);
  const response = await ops.post(`/api/v1/operations/disputes/${disputeId}/escalate`, { headers: { Authorization: `Bearer ${token}` }, maxRedirects: 0 });
  expect(response.status()).toBe(409);
  const body = await response.json();
  expect(expectProblemShape(body, 'action-not-allowed', 409)).toEqual([]);
  expect(body.disputeStatus).toBe('escalated');
  expect(eventsOf(disputeId)).toEqual(before);
  expect(eventsOf(disputeId).filter((event) => event.EventType === 'DisputeEscalated')).toHaveLength(1);
  await ops.dispose();
});

test('across every dispute: at most one escalation and one warning, the sweep acts only on open disputes, no personal data in its stored events', async () => {
  const counts = rows<{ DisputeId: string; EventType: string; N: number }>(
    `select "DisputeId", "EventType", count(*)::int as "N" from "StoredEvents" where "EventType" in ('DisputeEscalated', 'SlaWarningRaised') group by 1, 2 having count(*) > 1`);
  expect(counts).toEqual([]);
  const sweepEscalations = rows(`select "Data" from "StoredEvents" where "ActorId" = 'worker:sla-sweep' and "EventType" = 'DisputeEscalated'`);
  for (const event of sweepEscalations) expect(['submitted', 'underReview']).toContain(event.Data.fromStatus);
  // Resolved, Withdrawn and already escalated disputes past their SLA carry no sweep event.
  const closedByOthers = rows(
    `select d."Id" from "Disputes" d where d."Status" in ('Resolved', 'Withdrawn') and exists (select 1 from "StoredEvents" e where e."DisputeId" = d."Id" and e."ActorId" = 'worker:sla-sweep' and e."EventType" = 'DisputeEscalated' and e."Version" > (select max(x."Version") from "StoredEvents" x where x."DisputeId" = d."Id" and x."EventType" in ('DisputeResolved', 'DisputeWithdrawn')))`);
  expect(closedByOthers).toEqual([]);
  const sweepData = JSON.stringify(rows(`select "Data" from "StoredEvents" where "ActorId" = 'worker:sla-sweep'`));
  expect(sweepData).not.toMatch(/CUS-|ACC-|TXN-/);
});

test('the heartbeat is fresh, the Worker is healthy, and its logs hold counts and IDs only', async () => {
  expect(workerHealth()).toBe('healthy');
  const busybox = (...args: string[]) => Number(docker('exec', worker, '/usr/local/bin/busybox', ...args));
  const age = busybox('date', '+%s') - busybox('stat', '-c', '%Y', '/tmp/heartbeat-sweep');
  expect(age).toBeLessThanOrEqual(35);
  const log = docker('logs', worker);
  expect(log).toMatch(/SLA sweep (Overdue|Warning) batch changed \d+ of \d+ claimed disputes/);
  expect(log).not.toMatch(/CUS-|ACC-|TXN-|Password=|Bearer /);
  expect(log).not.toMatch(/SLA sweep could not change|SLA sweep loop failed/);
});

test('restarting the Worker after the escalation gives no duplicate, and the Worker is healthy again', async () => {
  test.setTimeout(240_000);
  const before = rows(`select count(*)::int as "N" from "StoredEvents"`)[0].N;
  const outboxBefore = rows(`select count(*)::int as "N" from "OutboxMessages"`)[0].N;
  docker('restart', worker);
  const deadline = Date.now() + 120_000;
  while (workerHealth() !== 'healthy' && Date.now() < deadline) await sleep(3000);
  expect(workerHealth()).toBe('healthy');
  await sleep(70_000); // two more loops of the sweep
  expect(eventsOf(disputeId).filter((event) => event.EventType === 'DisputeEscalated')).toHaveLength(1);
  expect(eventsOf(disputeId).filter((event) => event.EventType === 'SlaWarningRaised')).toHaveLength(1);
  expect(rows(`select count(*)::int as "N" from "StoredEvents"`)[0].N).toBe(before);
  expect(rows(`select count(*)::int as "N" from "OutboxMessages"`)[0].N).toBe(outboxBefore);
  expect(topicRecords().filter((record) => record.key === disputeId && JSON.parse(record.value).data.action === 'escalate')).toHaveLength(1);
});
