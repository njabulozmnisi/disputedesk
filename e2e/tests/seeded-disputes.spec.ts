import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, test, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase, topicRecords } from './support';

// Story 5.4: the seeder's disputes for customers A and B, on the real stack, through the SPA, the gateway, the API, the
// database (qa_readonly) and the topic. Expectations come from fixtures/local.json, so the spec follows the fixture.
// It only reads the seeded rows (and sends one refused withdraw), so it is safe to run on a database other specs have
// used, but the "once on the topic" and "restart" tests assume the stack's own Seed:Enabled run.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const dashboard = process.env.ASPIRE_ORIGIN ?? 'http://localhost:18888';
const fixtures = JSON.parse(readFileSync(fileURLToPath(new URL('../../fixtures/local.json', import.meta.url)), 'utf8'));

type FixtureAction = { action: string; outcome?: string; note?: string };
type FixtureDispute = { customer: string; accountRef: string; transactionId: string; reason: string; note?: string; slaDemo?: boolean; actions: FixtureAction[] };
const seeded: FixtureDispute[] = fixtures.disputes;
const forCustomer = (key: string) => seeded.filter((entry) => entry.customer === key);
const transactionList = (entries: FixtureDispute[]) => entries.map((entry) => `'${entry.transactionId}'`).join(',');

const statusAfter: Record<string, string> = { raise: 'Submitted', startReview: 'UnderReview', escalate: 'Escalated', resolve: 'Resolved', withdraw: 'Withdrawn' };
const eventTypeOf: Record<string, string> = { raise: 'DisputeRaised', startReview: 'ReviewStarted', escalate: 'DisputeEscalated', resolve: 'DisputeResolved', withdraw: 'DisputeWithdrawn' };
// Story 5.3: the SLA demo dispute falls due 5 minutes after seeding and the Worker's sweep then records SlaWarningRaised
// (at the first loop, it is inside the 48-hour lead time) and escalates it as worker:sla-sweep, so beforeAll waits for that and
// the demo dispute is expected Escalated, with those two events after its seeded actions (and one more outbox row).
const sweepEventTypes = ['SlaWarningRaised', 'DisputeEscalated'];
const finalStatus = (entry: FixtureDispute) => (entry.slaDemo ? 'Escalated' : statusAfter[entry.actions[entry.actions.length - 1].action]);
const customerActions = ['raise', 'withdraw'];

// The customer-facing name of each status (Escalated is shown as prioritised, as story 3.7 decides).
const customerStatusOf: Record<string, string> = { Submitted: 'submitted', UnderReview: 'underReview', Escalated: 'underReviewPrioritised', Withdrawn: 'withdrawn', Resolved: 'resolved' };
const badgeOf = (status: string, outcome: string | null) =>
  ({ submitted: 'Submitted', underReview: 'Under review', underReviewPrioritised: 'Under review (prioritised)', withdrawn: 'Withdrawn' } as Record<string, string>)[status]
  ?? `Resolved · ${outcome === 'upheld' ? 'Upheld' : 'Rejected'}`;

function rows<T>(sql: string): T[] {
  return JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
}
const docker = (...args: string[]) => execFileSync('docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });

const loginUserId = (customerKey: string) => fixtures.logins.find((login: { key: string }) => login.key === `customer-${customerKey.toLowerCase()}`).userId as string;

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

type Item = { disputeId: string; customerStatus: string; outcome: string | null; reason: string; transaction: { transactionId: string; description: string }; createdAt: string };
async function history(context: BrowserContext): Promise<Item[]> {
  const items: Item[] = [];
  let cursor: string | null = null;
  do {
    await new Promise((resolve) => setTimeout(resolve, 150));
    const response = await context.request.get(`/api/v1/disputes?limit=50${cursor === null ? '' : `&cursor=${cursor}`}`);
    expect(response.status()).toBe(200);
    const page = (await response.json()) as { items: Item[]; nextCursor: string | null };
    items.push(...page.items);
    cursor = page.nextCursor;
  } while (cursor !== null);
  return items;
}

let aHistory: Item[];
let bHistory: Item[];
const seededOf = (items: Item[], entries: FixtureDispute[]) => items.filter((item) => entries.some((entry) => entry.transactionId === item.transaction.transactionId));

test.beforeAll(async ({ browser }) => {
  test.setTimeout(600_000);
  const demoTransaction = seeded.find((entry) => entry.slaDemo)!.transactionId;
  const deadline = Date.now() + 8 * 60_000;
  while (rows(`select 1 from "Disputes" where "TransactionId" = '${demoTransaction}' and "Status" = 'Escalated'`).length === 0 && Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 3000));
  }
  customerA = await signIn(browser, 'customer-a');
  customerB = await signIn(browser, 'customer-b');
  aHistory = await history(customerA);
  bHistory = await history(customerB);
});
test.afterAll(async () => {
  await customerA?.close();
  await customerB?.close();
});

test.describe.serial('seeded disputes', () => {
  test('A has one dispute per status and outcome, in the history API and in the SPA', async () => {
    const mine = seededOf(aHistory, forCustomer('A'));
    expect(mine).toHaveLength(forCustomer('A').length);
    const shown = mine.map((item) => badgeOf(item.customerStatus, item.outcome));
    expect([...new Set(shown)].sort()).toEqual(
      ['Resolved · Rejected', 'Resolved · Upheld', 'Submitted', 'Under review', 'Under review (prioritised)', 'Withdrawn'].sort());
    for (const entry of forCustomer('A')) {
      const item = mine.find((candidate) => candidate.transaction.transactionId === entry.transactionId)!;
      const last = entry.actions[entry.actions.length - 1];
      expect(item.customerStatus, entry.transactionId).toBe(customerStatusOf[finalStatus(entry)]);
      expect(item.outcome, entry.transactionId).toBe(last.action === 'resolve' ? last.outcome : null);
      expect(Object.keys(item).sort()).toEqual(['createdAt', 'customerStatus', 'disputeId', 'disputedAmount', 'outcome', 'reason', 'transaction']);
    }

    const page = await customerA.newPage();
    await page.goto('/disputes');
    await expect(page.getByRole('heading', { level: 1, name: 'Disputes', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Load more' }).click({ timeout: 2_000 }).catch(() => undefined);
    for (const item of mine) {
      await expect(page.getByRole('link', { name: new RegExp(`${item.transaction.description}.*${badgeOf(item.customerStatus, item.outcome).replace(/[()·]/g, '.')}`, 's') }).first()).toBeVisible();
    }
    await page.close();
  });

  test('each seeded dispute replays to its row and its timeline', async () => {
    for (const entry of forCustomer('A').concat(forCustomer('B'))) {
      const [row] = rows<{ Id: string; Status: string; Outcome: string | null; Version: number }>(
        `select "Id","Status","Outcome","Version" from "Disputes" where "TransactionId" = '${entry.transactionId}'`);
      const events = rows<{ Version: number; EventType: string; ActorType: string; ActorId: string; Data: { toStatus: string; outcome?: string } }>(
        `select "Version","EventType","ActorType","ActorId","Data" from "StoredEvents" where "DisputeId" = '${row.Id}' order by "Version"`);
      const swept = entry.slaDemo ? sweepEventTypes : [];
      expect(events.map((event) => event.EventType), entry.transactionId).toEqual([...entry.actions.map((action) => eventTypeOf[action.action]), ...swept]);
      expect(events.map((event) => event.Version)).toEqual(events.map((_, index) => index + 1));
      expect(row.Version, `${entry.transactionId} version`).toBe(events.length);
      expect(events[events.length - 1].Data.toStatus.toLowerCase()).toBe(row.Status.toLowerCase());
      expect(row.Status).toBe(finalStatus(entry));
      const resolved = entry.actions.find((action) => action.action === 'resolve');
      expect(row.Outcome).toBe(resolved ? resolved.outcome![0].toUpperCase() + resolved.outcome!.slice(1) : null);
      events.forEach((event, index) => {
        if (index >= entry.actions.length) {
          expect(event).toMatchObject({ ActorType: 'Automation', ActorId: 'worker:sla-sweep' });
          return;
        }
        const customer = customerActions.includes(entry.actions[index].action);
        expect(event.ActorType, `${entry.transactionId} event ${index + 1}`).toBe(customer ? 'Customer' : 'Automation');
        expect(event.ActorId).toBe(customer ? loginUserId(entry.customer) : 'worker:seeder');
      });

      if (entry.customer !== 'A') continue;
      const detail = await customerA.request.get(`/api/v1/disputes/${row.Id}`);
      expect(detail.status()).toBe(200);
      const body = await detail.json();
      // The warning is never a timeline step; the sweep's escalation is.
      expect(body.timeline.map((step: { action: string }) => step.action)).toEqual([...entry.actions.map((action) => action.action), ...(entry.slaDemo ? ['escalate'] : [])]);
      expect(body.customerStatus).toBe(customerStatusOf[finalStatus(entry)]);
    }
  });

  test('the SLA demo dispute was escalated by the sweep, and falls due 5 minutes after the seeding instant, 240 hours after its raise', () => {
    const [sla] = rows<{ Status: string; Version: number; created: string; due: string; hours: number; micros: number; firstSent: string }>(
      `select d."Status", d."Version", d."CreatedAtUtc" as created, d."SlaDueAtUtc" as due,
         extract(epoch from (d."SlaDueAtUtc" - d."CreatedAtUtc")) / 3600 as hours,
         extract(microseconds from d."SlaDueAtUtc")::bigint % 1000000 as micros,
         (select min(o."SentAtUtc") from "OutboxMessages" o where o."DisputeId" = d."Id") as "firstSent"
       from "Disputes" d where d."TransactionId" = '${seeded.find((entry) => entry.slaDemo)!.transactionId}'`);
    expect(sla.Status).toBe('Escalated');
    expect(sla.hours).toBe(240);
    const seedingInstant = new Date(sla.due).getTime() - 5 * 60_000;
    const sent = new Date(sla.firstSent).getTime();
    expect(sent - seedingInstant, 'the relay sent the raise shortly after seeding').toBeGreaterThan(0);
    expect(sent - seedingInstant).toBeLessThan(5 * 60_000);
    const [old] = rows<{ days: number }>(`select extract(epoch from (d."CreatedAtUtc" - d."TransactionDateUtc")) / 86400 as days from "Disputes" d where d."TransactionId" = '${seeded.find((entry) => entry.slaDemo)!.transactionId}'`);
    expect(old.days).toBeGreaterThanOrEqual(0);
  });

  test('B owns the dispute A gets 404 for, identical to an unknown ID apart from traceId', async () => {
    const mine = seededOf(bHistory, forCustomer('B'));
    expect(mine).toHaveLength(1);
    expect(mine[0].transaction.transactionId).toBe(forCustomer('B')[0].transactionId);
    expect(seededOf(aHistory, forCustomer('B'))).toEqual([]);

    const own = await customerB.request.get(`/api/v1/disputes/${mine[0].disputeId}`);
    expect(own.status()).toBe(200);
    const foreign = await customerA.request.get(`/api/v1/disputes/${mine[0].disputeId}`);
    const unknown = await customerA.request.get('/api/v1/disputes/01a00000-0000-7000-8000-000000000000');
    expect(foreign.status()).toBe(404);
    expect(unknown.status()).toBe(404);
    const foreignBody = await foreign.json();
    const unknownBody = await unknown.json();
    expect(expectProblemShape(foreignBody, 'not-found', 404)).toEqual([]);
    const { traceId: _a, ...foreignRest } = foreignBody;
    const { traceId: _b, ...unknownRest } = unknownBody;
    expect(foreignRest).toEqual(unknownRest);
    expect(foreign.headers()['content-type']).toBe(unknown.headers()['content-type']);
    expect(JSON.stringify(foreignBody)).not.toContain(forCustomer('B')[0].transactionId);

    const token = (await (await customerA.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    const withdraw = await customerA.request.post(`/api/v1/disputes/${mine[0].disputeId}/withdraw`, { headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' }, data: '{}', maxRedirects: 0 });
    expect(withdraw.status()).toBe(404);
  });

  test('withdrawing a resolved seeded dispute is 409 and adds no event', async () => {
    const resolved = seededOf(aHistory, forCustomer('A')).find((item) => item.customerStatus === 'resolved')!;
    const before = queryDatabase(`select count(*) from "StoredEvents" where "DisputeId" = '${resolved.disputeId}'`);
    const token = (await (await customerA.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    const response = await customerA.request.post(`/api/v1/disputes/${resolved.disputeId}/withdraw`, { headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' }, data: '{}', maxRedirects: 0 });
    expect(response.status()).toBe(409);
    expect(response.headers()['content-type']).toContain('application/problem+json');
    expect(queryDatabase(`select count(*) from "StoredEvents" where "DisputeId" = '${resolved.disputeId}'`)).toBe(before);
  });

  test('one stored event and one outbox row per seeded action, all sent, no audit row from seeding', () => {
    const ids = rows<{ Id: string }>(`select "Id" from "Disputes" where "TransactionId" in (${transactionList(seeded)})`).map((row) => `'${row.Id}'`).join(',');
    const expected = seeded.reduce((sum, entry) => sum + entry.actions.length, 0);
    // The sweep's two events on the demo dispute: the warning (stored only) and the escalation (stored and published).
    expect(Number(queryDatabase(`select count(*) from "StoredEvents" where "DisputeId" in (${ids})`))).toBe(expected + 2);
    expect(Number(queryDatabase(`select count(*) from "OutboxMessages" where "DisputeId" in (${ids})`))).toBe(expected + 1);
    expect(Number(queryDatabase(`select count(*) from "OutboxMessages" o join "StoredEvents" e on e."Id" = o."Id" and e."DisputeId" = o."DisputeId" and e."Version" = o."DisputeVersion" where o."DisputeId" in (${ids})`))).toBe(expected + 1);
    expect(Number(queryDatabase(`select count(*) from "OutboxMessages" where "DisputeId" in (${ids}) and "Status" <> 'Sent'`))).toBe(0);
    expect(Number(queryDatabase(`select count(*) from "AuditLogEntries" where ("DisputeId" in (${ids}) and not ("Action" = 'Withdraw' and "Result" = 'Rejected')) or "ActorId" = 'worker:seeder'`))).toBe(0);
    // Only the two refused withdraws of the tests above are audited (refused actions are, ad-009); seeding wrote none.
  });

  test('each seeded outbox id is on the topic exactly once, keyed by the dispute ID, with trace context', () => {
    const outbox = rows<{ Id: string; DisputeId: string }>(
      `select o."Id", o."DisputeId" from "OutboxMessages" o join "Disputes" d on d."Id" = o."DisputeId" where d."TransactionId" in (${transactionList(seeded)})`);
    const records = topicRecords().map((record) => ({ record, id: (JSON.parse(record.value) as { id: string }).id }));
    for (const row of outbox) {
      const matches = records.filter((entry) => entry.id === row.Id);
      expect(matches, `outbox ${row.Id}`).toHaveLength(1);
      expect(matches[0].record.key).toBe(row.DisputeId);
      expect(matches[0].record.headers.map((header) => header.key)).toContain('traceparent');
    }
  });

  test('no customer note, ID number, account number or name in the logs or the telemetry', async () => {
    const secrets = new Set<string>();
    for (const customer of fixtures.customers) {
      for (const field of ['idNumber', 'cellphone', 'displayName']) if (customer[field]) secrets.add(String(customer[field]));
      for (const account of customer.accounts ?? []) if (account.accountNumber) secrets.add(String(account.accountNumber));
    }
    for (const entry of seeded) {
      if (entry.note) secrets.add(entry.note);
      for (const action of entry.actions) if (action.note) secrets.add(action.note);
    }
    expect(secrets.size).toBeGreaterThan(8);
    const haystacks: Record<string, string> = {};
    for (const container of ['disputedesk-api-1', 'disputedesk-worker-1', 'disputedesk-core-banking-1']) {
      haystacks[container] = docker('logs', container);
    }
    for (const kind of ['spans', 'logs']) {
      const response = await fetch(`${dashboard}/api/telemetry/${kind}`, { signal: AbortSignal.timeout(20_000) });
      haystacks[`telemetry ${kind}`] = await response.text();
    }
    for (const [name, text] of Object.entries(haystacks)) {
      expect(text.length, name).toBeGreaterThan(0);
      for (const secret of secrets) expect(text.includes(secret), `${name} holds a fixture value`).toBe(false);
    }
    const lines = haystacks['disputedesk-api-1'].split('\n');
    const seederText = lines.flatMap((line, index) => (line.includes('Seeder[') ? [lines[index + 1] ?? ''] : []));
    expect(seederText.length).toBeGreaterThan(0);
    for (const line of seederText) expect(line).toMatch(/^\s*Seeded \d+ (customer links|disputes)$/);
  });

  test('a restart of the Api adds no outbox row and repeats only ids already seen', async () => {
    const ids = rows<{ Id: string }>(`select o."Id" from "OutboxMessages" o`).map((row) => row.Id);
    const counts = () => queryDatabase('select (select count(*) from "Disputes") || \'/\' || (select count(*) from "StoredEvents") || \'/\' || (select count(*) from "OutboxMessages")');
    const before = counts();
    docker('restart', 'disputedesk-api-1');
    await expect.poll(async () => (await fetch(`${origin}/api/health/ready`).catch(() => ({ status: 0 }))).status, { timeout: 120_000, intervals: [2_000] }).toBe(200);
    await new Promise((resolve) => setTimeout(resolve, 15_000));
    expect(counts()).toBe(before);
    const seen = new Set(ids);
    for (const record of topicRecords()) expect(seen.has((JSON.parse(record.value) as { id: string }).id)).toBe(true);
  });
});
