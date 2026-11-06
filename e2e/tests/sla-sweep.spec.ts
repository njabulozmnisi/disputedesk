import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type APIRequestContext, type BrowserContext } from '@playwright/test';
import { expectProblemShape, topicRecords, origin, queryRows, signInContext } from './support';

// Story 5.3: the SLA sweep on the real stack, run inside the api locally. The seeded SLA demo dispute (customer A, due 5 minutes after the
// seeding instant) is escalated by the sweep as `automation` / `worker:sla-sweep`. The sweep only escalates, so
// the stored stream and the outbox hold versions 1, 2, 3 with no gap, and there is no warning column or index. Four hops: the SPA badge, the API response, the database (qa_readonly) and the topic.
// Needs a fresh stack with the committed fixture (the demo dispute is due at seed time + 5 minutes); the spec waits for it.
// The spec reads only. The one write is the operations escalate that is refused on the already escalated demo dispute.

const root = new URL('../..', import.meta.url).pathname;
const fixtures = JSON.parse(readFileSync(fileURLToPath(new URL('../../backend/fixtures/local.json', import.meta.url)), 'utf8'));
const demo = fixtures.disputes.find((entry: { slaDemo?: boolean }) => entry.slaDemo) as { accountRef: string; transactionId: string };
const api = 'disputedesk-api-1';

type Json = Record<string, any>;

const docker = (...args: string[]) => execFileSync('docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const apiHealth = () => docker('inspect', api, '--format', '{{.State.Health.Status}}');
const sleep = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

const eventsOf = (id: string) =>
  queryRows<Json>(`select "Id", "Version", "EventType", "ActorType", "ActorId", to_char("OccurredAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') as "At", "Data" from "StoredEvents" where "DisputeId" = '${id}' order by "Version"`);
const outboxOf = (id: string) => queryRows<Json>(`select "Id", "EventType", "DisputeVersion", "Payload", "TraceParent" from "OutboxMessages" where "DisputeId" = '${id}' order by "DisputeVersion"`);
// The relay can still be sending (after a broker outage, for example) when the Disputes row turns Escalated, so the
// topic is read only once every outbox row of the stream is Sent.
const untilStreamSent = (id: string) =>
  expect.poll(() => queryRows<Json>(`select "Status" from "OutboxMessages" where "DisputeId" = '${id}' order by "DisputeVersion"`).map((row) => row.Status), { timeout: 45_000 })
    .toEqual(['Sent', 'Sent', 'Sent']);

let disputeId: string;
let dueAt: string;
let customerA: BrowserContext;
let customerB: BrowserContext;

test.beforeAll(async ({ browser }) => {
  test.setTimeout(600_000);
  const [row] = queryRows<{ Id: string; Due: string }>(
    `select "Id", to_char("SlaDueAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') as "Due" from "Disputes" where "AccountRef" = '${demo.accountRef}' and "TransactionId" = '${demo.transactionId}'`);
  expect(row, 'the seeded SLA demo dispute').toBeDefined();
  disputeId = row.Id;
  dueAt = row.Due;
  customerA = await signInContext(browser, 'customer-a');
  customerB = await signInContext(browser, 'customer-b');
  // Waits for the sweep: up to the due time plus two 30-second loops.
  const deadline = Date.now() + 8 * 60_000;
  while (Date.now() < deadline) {
    if (queryRows<Json>(`select 1 from "Disputes" where "Id" = '${disputeId}' and "Status" = 'Escalated'`).length > 0) return;
    await sleep(3000); // the gap between two reads of the Disputes row while the sweep runs
  }
});

test('the sweep escalated the demo dispute within one loop of its due time, once, as automation', async () => {
  const events = eventsOf(disputeId);
  expect(events.map((event) => event.EventType)).toEqual(['DisputeRaised', 'ReviewStarted', 'DisputeEscalated']);
  expect(events.map((event) => event.Version)).toEqual([1, 2, 3]);
  const escalated = events[2];
  expect(escalated).toMatchObject({ ActorType: 'Automation', ActorId: 'worker:sla-sweep' });
  expect(escalated.Data).toEqual({ fromStatus: 'underReview', toStatus: 'escalated' });
  // Not before the SLA is due (FR-26), and within a 30-second loop of it (plus the batch's own time).
  const lateBySeconds = (Date.parse(`${escalated.At}Z`) - Date.parse(`${dueAt}Z`)) / 1000;
  expect(lateBySeconds).toBeGreaterThanOrEqual(0);
  expect(lateBySeconds).toBeLessThanOrEqual(35);
  const [row] = queryRows<Json>(`select "Status", "Version" from "Disputes" where "Id" = '${disputeId}'`);
  expect(row).toEqual({ Status: 'Escalated', Version: 3 });
  // No audit row for an SLA escalation (NFR-SEC-10).
  expect(queryRows<Json>(`select 1 from "AuditLogEntries" where "DisputeId" = '${disputeId}' and "ActorType" = 'Automation'`)).toHaveLength(0);
});

test('no warning anywhere: the DropSlaWarning migration applied, the stream and the outbox have versions 1..n with no gap, the topic agrees', async () => {
  const events = eventsOf(disputeId);
  expect(events.filter((event) => event.EventType === 'SlaWarningRaised')).toHaveLength(0);
  expect(queryRows<Json>(`select 1 from "StoredEvents" where "EventType" = 'SlaWarningRaised'`)).toHaveLength(0);
  expect(queryRows<Json>(`select 1 from information_schema.columns where table_name = 'Disputes' and column_name = 'SlaWarningRaisedAtUtc'`)).toHaveLength(0);
  expect(queryRows<Json>(`select 1 from pg_indexes where tablename = 'Disputes' and indexname = 'IX_Disputes_SlaWarning'`)).toHaveLength(0);
  expect(queryRows<Json>(`select 1 from "__EFMigrationsHistory" where "MigrationId" like '%_DropSlaWarning'`)).toHaveLength(1);
  // Every stored event is published: one outbox row per event, with the stored event's ID and version.
  const outbox = outboxOf(disputeId);
  expect(outbox.map((message) => message.DisputeVersion)).toEqual([1, 2, 3]);
  expect(outbox.map((message) => message.Id)).toEqual(events.map((event) => event.Id));
  await untilStreamSent(disputeId);
  const onTopic = topicRecords().filter((record) => record.key === disputeId).map((record) => JSON.parse(record.value) as Json);
  expect(onTopic.map((record) => record.data.disputeVersion).sort()).toEqual([1, 2, 3]);
  expect(onTopic.map((record) => record.id).sort()).toEqual(events.map((event) => event.Id).sort());
});

test('one status-changed event with actor type automation and no actor identity: outbox row and topic record agree', async () => {
  const events = eventsOf(disputeId);
  const escalated = events[2];
  const rowsOfEscalation = outboxOf(disputeId).filter((message) => message.Id === escalated.Id);
  expect(rowsOfEscalation).toHaveLength(1);
  const [message] = rowsOfEscalation;
  expect(message).toMatchObject({ EventType: 'disputedesk.dispute.status-changed.v1', DisputeVersion: 3 });
  expect(message.TraceParent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/);
  const published = JSON.parse(message.Payload);
  expect(published.data).toMatchObject({ disputeId, fromStatus: 'underReview', toStatus: 'escalated', action: 'escalate', actorType: 'automation', disputeVersion: 3 });
  expect(JSON.stringify(published)).not.toMatch(/worker:sla-sweep|actorId|"sub"/);

  await untilStreamSent(disputeId);
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

test('the customer sees one Under review (prioritised) entry from the bank, from the bank; another customer gets 404', async () => {
  const response = await customerA.request.get(`/api/v1/disputes/${disputeId}`);
  expect(response.status()).toBe(200);
  const text = await response.text();
  const body = JSON.parse(text) as Json;
  expect(body.customerStatus).toBe('underReviewPrioritised');
  const prioritised = body.timeline.filter((entry: Json) => entry.customerStatus === 'underReviewPrioritised');
  expect(prioritised).toHaveLength(1);
  expect(prioritised[0]).toMatchObject({ action: 'escalate', actor: 'theBank' });
  expect(body.timeline).toHaveLength(3); // raise, start review, escalate
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

test('across every dispute: at most one escalation, the sweep acts only on open disputes, no personal data in its stored events', async () => {
  const counts = queryRows<{ DisputeId: string; EventType: string; N: number }>(
    `select "DisputeId", "EventType", count(*)::int as "N" from "StoredEvents" where "EventType" = 'DisputeEscalated' group by 1, 2 having count(*) > 1`);
  expect(counts).toEqual([]);
  const sweepEscalations = queryRows<Json>(`select "Data" from "StoredEvents" where "ActorId" = 'worker:sla-sweep' and "EventType" = 'DisputeEscalated'`);
  for (const event of sweepEscalations) expect(['submitted', 'underReview']).toContain(event.Data.fromStatus);
  // Resolved, Withdrawn and already escalated disputes past their SLA carry no sweep event.
  const closedByOthers = queryRows<Json>(
    `select d."Id" from "Disputes" d where d."Status" in ('Resolved', 'Withdrawn') and exists (select 1 from "StoredEvents" e where e."DisputeId" = d."Id" and e."ActorId" = 'worker:sla-sweep' and e."EventType" = 'DisputeEscalated' and e."Version" > (select max(x."Version") from "StoredEvents" x where x."DisputeId" = d."Id" and x."EventType" in ('DisputeResolved', 'DisputeWithdrawn')))`);
  expect(closedByOthers).toEqual([]);
  const sweepData = JSON.stringify(queryRows<Json>(`select "Data" from "StoredEvents" where "ActorId" = 'worker:sla-sweep'`));
  expect(sweepData).not.toMatch(/CUS-|ACC-|TXN-/);
});

test('the heartbeat is fresh, the api is healthy, and its logs hold counts and IDs only', async () => {
  expect(apiHealth()).toBe('healthy');
  const busybox = (...args: string[]) => Number(docker('exec', api, '/usr/local/bin/busybox', ...args));
  const age = busybox('date', '+%s') - busybox('stat', '-c', '%Y', '/tmp/heartbeat-sweep');
  expect(age).toBeLessThanOrEqual(35);
  const log = docker('logs', api);
  expect(log).toMatch(/SLA sweep batch changed \d+ of \d+ claimed disputes/);
  expect(log).not.toMatch(/CUS-|ACC-|TXN-|Password=|Bearer /);
  expect(log).not.toMatch(/SLA sweep could not change|SLA sweep loop failed/);
});

test('restarting the api after the escalation gives no duplicate, and the api is healthy again', async () => {
  test.setTimeout(240_000);
  const before = queryRows<Json>(`select count(*)::int as "N" from "StoredEvents"`)[0].N;
  const outboxBefore = queryRows<Json>(`select count(*)::int as "N" from "OutboxMessages"`)[0].N;
  docker('restart', api);
  const deadline = Date.now() + 120_000;
  while (apiHealth() !== 'healthy' && Date.now() < deadline) await sleep(3000); // the gap between two health checks of the restarted api
  expect(apiHealth()).toBe('healthy');
  // The gateway finds the restarted api again only after nginx's lookup retry delay (docker-compose.yml, gateway).
  await expect.poll(async () => (await fetch(`${origin}/api/v1/authentication/session`).catch(() => null))?.status, { timeout: 90_000, intervals: [1000] }).toBe(200);
  await sleep(70_000); // two more loops of the sweep
  expect(eventsOf(disputeId).filter((event) => event.EventType === 'DisputeEscalated')).toHaveLength(1);
  expect(queryRows<Json>(`select count(*)::int as "N" from "StoredEvents"`)[0].N).toBe(before);
  expect(queryRows<Json>(`select count(*)::int as "N" from "OutboxMessages"`)[0].N).toBe(outboxBefore);
  expect(topicRecords().filter((record) => record.key === disputeId && JSON.parse(record.value).data.action === 'escalate')).toHaveLength(1);
});
