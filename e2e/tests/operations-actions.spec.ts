import { execFileSync } from 'node:child_process';
import { expect, request as playwrightRequest, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase, topicRecords } from './support';

// Story 5.2: operations start review, escalate and resolve under the FR-24 table, with a bearer token from
// scripts/operations-token.sh. Network level with the real Zitadel token, the database through qa_readonly, the events
// from the disputedesk.disputes topic, and the customer's view through the BFF session and the SPA history screen.
// The 403 cases need tokens with other roles: the spec creates throwaway machine users in Zitadel with the stack's
// admin PAT (read from the bootstrap volume; it never reaches a command line or the evidence). Those users are not
// mapped to an operations actor, which is itself one of the 403 cases. Raises are spread over the seeded customers
// because the data is not reset between runs and a transaction can be disputed once. Customer A's 20 disputable
// transactions are all needed by the other specs, so this spec raises on customer B only (7 of B's 20).

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const authOrigin = process.env.AUTH_ORIGIN ?? 'http://auth.localhost:8080';
const root = new URL('../..', import.meta.url).pathname;
const unknownId = '01a189e2-0000-7000-8000-000000000052';
const noteMarker = 'qa52-note-marker-never-in-telemetry';
const customerNote = 'qa52-customer-raise-note';

type Json = Record<string, any>;
type Action = 'start-review' | 'escalate' | 'resolve';
const auditAction: Record<Action, string> = { 'start-review': 'StartReview', escalate: 'Escalate', resolve: 'Resolve' };
const wireAction: Record<Action, string> = { 'start-review': 'startReview', escalate: 'escalate', resolve: 'resolve' };
const storedType: Record<Action, string> = { 'start-review': 'ReviewStarted', escalate: 'DisputeEscalated', resolve: 'DisputeResolved' };

const contexts: Record<string, BrowserContext> = {};
let ops: APIRequestContext;
let token: string;
let sub: string;
let machine: Record<'noRole' | 'readOnly' | 'unmapped', { token: string; sub: string }>;
const ids: Record<string, string> = {};
const owner: Record<string, string> = {};

const decode = (jwt: string) => JSON.parse(Buffer.from(jwt.split('.')[1], 'base64url').toString('utf8')) as Json;
const bearer = (value = token) => ({ Authorization: `Bearer ${value}` });
const withoutTraceId = ({ traceId: _ignored, ...rest }: Json) => rest;
const instant = (value: string) => value.replace(/(\+00:00|Z)$/, '').replace(/(\.\d*?)0+$/, '$1').replace(/\.$/, '');

function rows<T = Json>(sql: string): T[] {
  return JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
}
const events = (id: string) => rows(`select "Id", "Version", "EventType", "ActorType", "ActorId", to_char("OccurredAtUtc" at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US') as "At", "Data" from "StoredEvents" where "DisputeId" = '${id}' order by "Version"`);
const outbox = (id: string) => rows(`select "Id", "EventType", "DisputeVersion", "Payload", "TraceParent" from "OutboxMessages" where "DisputeId" = '${id}' order by "DisputeVersion"`);
const disputeRow = (id: string) => rows(`select "Status", "Outcome", "Version" from "Disputes" where "Id" = '${id}'`)[0];
const audit = (id: string) => rows(`select "Action", "Result", "ActorType", "ActorId", "CustomerId", "DisputeId" from "AuditLogEntries" where "DisputeId" = '${id}' order by "OccurredAtUtc", "Id"`);
const auditCount = () => Number(queryDatabase('select count(*) from "AuditLogEntries"'));

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

/** Raises a dispute on the first of the given customers that still has a disputable transaction. */
async function raise(...logins: string[]): Promise<{ id: string; login: string }> {
  for (const login of logins) {
    const api = contexts[login].request;
    const list = (await (await api.get('/api/v1/transactions?limit=50')).json()).items as { transactionId: string; accountRef: string; disputable: boolean }[];
    const row = list.find((item) => item.disputable);
    if (!row) continue;
    const csrf = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    const response = await api.post('/api/v1/disputes', {
      headers: { 'X-XSRF-TOKEN': csrf, 'Idempotency-Key': `"qa52-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
      data: { accountRef: row.accountRef, transactionId: row.transactionId, reason: 'unrecognised', note: customerNote },
      maxRedirects: 0,
    });
    expect(response.status()).toBe(201);
    return { id: response.headers().location.split('/').pop()!, login };
  }
  throw new Error(`no disputable transaction left for ${logins.join(', ')} (recreate the volumes)`);
}

// The gateway's coarse limiter (infra/nginx: 10 requests a second per client, burst 20) answers 429 to a faster loop, so
// the calls are spaced. The race tests send their pair at once, inside the burst.
let lastCall = 0;
async function pace() {
  const wait = lastCall + 130 - Date.now();
  if (wait > 0) await new Promise((resolve) => setTimeout(resolve, wait));
  lastCall = Date.now();
}

type Send = { headers?: Record<string, string>; json?: unknown; raw?: string; contentType?: string; unpaced?: boolean };
async function act(id: string, action: Action, send: Send = {}, api: APIRequestContext = ops) {
  if (!send.unpaced) await pace();
  const headers = { ...(send.headers ?? bearer()) };
  const options: { headers: Record<string, string>; data?: unknown; maxRedirects: number } = { headers, maxRedirects: 0 };
  if (send.raw !== undefined) {
    headers['Content-Type'] = send.contentType ?? 'application/json';
    options.data = send.raw;
  } else if (send.json !== undefined) {
    headers['Content-Type'] = 'application/json';
    options.data = JSON.stringify(send.json);
  }
  return api.post(`/api/v1/operations/disputes/${id}/${action}`, options);
}
const readOps = async (id: string, headers: Record<string, string> = bearer()) => { await pace(); return ops.get(`/api/v1/operations/disputes/${id}`, { headers, maxRedirects: 0 }); };
const resolveBody = (outcome: string, note: string) => ({ json: { outcome, note } });

/**
 * One action with the four-hop facts. A 200 must equal the following read, add exactly one stored event with the next
 * version, one outbox row with the same ID, and no audit row. A 409 must add one Rejected row and nothing else.
 */
async function step(id: string, action: Action, expectation: { status: 200; to: string; from: string; outcome?: string; note?: string } | { status: 409; disputeStatus: string }, send: Send = {}) {
  const beforeEvents = events(id);
  const beforeOutbox = outbox(id);
  const beforeAudit = audit(id);
  const response = await act(id, action, send);
  expect(response.status(), `${action} on ${id}`).toBe(expectation.status);
  const body = await response.json();

  if (expectation.status === 409) {
    expect(expectProblemShape(body, 'action-not-allowed', 409)).toEqual([]);
    expect(body.disputeStatus).toBe(expectation.disputeStatus);
    expect(body).not.toHaveProperty('customerStatus');
    expect(events(id)).toEqual(beforeEvents);
    expect(outbox(id)).toEqual(beforeOutbox);
    const added = audit(id).slice(beforeAudit.length);
    expect(added).toEqual([{ Action: auditAction[action], Result: 'Rejected', ActorType: 'Operations', ActorId: sub, CustomerId: null, DisputeId: id }]);
    expect(audit(id).length).toBe(beforeAudit.length + 1);
    return body;
  }

  // 200: the strict camelCase operations view, equal to the following read.
  expect(body.status).toBe(expectation.to);
  expect(Object.keys(body).sort()).toEqual(['createdAt', 'disputeId', 'disputedAmount', 'expectedAmount', 'outcome', 'reason', 'status', 'timeline', 'transaction']);
  const after = events(id);
  expect(after).toHaveLength(beforeEvents.length + 1);
  const added = after[after.length - 1];
  expect(added).toMatchObject({ Version: beforeEvents.length + 1, EventType: storedType[action], ActorType: 'Operations', ActorId: sub });
  expect(added.Data).toMatchObject({ fromStatus: expectation.from, toStatus: expectation.to });
  if (action !== 'resolve') expect(Object.keys(added.Data).sort()).toEqual(['fromStatus', 'toStatus']);
  const last = body.timeline[body.timeline.length - 1];
  expect(last).toMatchObject({ actorType: 'operations', fromStatus: expectation.from, toStatus: expectation.to });
  expect(instant(last.occurredAt)).toBe(instant(added.At));
  expect(body.timeline).toHaveLength(beforeEvents.length + 1);
  if (action === 'resolve') {
    expect(body.outcome).toBe(expectation.outcome);
    expect(last.note).toBe(expectation.note);
  } else {
    expect(last.note).toBeNull();
  }
  expect(audit(id)).toEqual(beforeAudit); // a success writes no audit row

  const dispute = disputeRow(id);
  expect(dispute.Status.toLowerCase()).toBe(expectation.to.toLowerCase());
  expect(dispute.Version).toBe(added.Version);

  const afterOutbox = outbox(id);
  expect(afterOutbox).toHaveLength(beforeOutbox.length + 1);
  const row = afterOutbox[afterOutbox.length - 1];
  expect(row.Id).toBe(added.Id); // same ID as the stored event, written in one transaction
  expect(row.EventType).toBe('disputedesk.dispute.status-changed.v1');
  expect(row.DisputeVersion).toBe(added.Version);
  const cloudEvent = JSON.parse(row.Payload);
  expect(cloudEvent).toMatchObject({ specversion: '1.0', id: added.Id, type: 'disputedesk.dispute.status-changed.v1', subject: id });
  const data: Json = cloudEvent.data;
  const expectedData: Json = { disputeId: id, fromStatus: expectation.from, toStatus: expectation.to, action: wireAction[action], actorType: 'operations', occurredAt: data.occurredAt, disputeVersion: added.Version };
  if (action === 'resolve') expectedData.outcome = expectation.outcome;
  expect(data).toEqual(expectedData);
  expect(instant(data.occurredAt)).toBe(instant(added.At));
  expect(row.Payload).not.toContain(sub);
  expect(row.Payload).not.toContain(noteMarker);
  if (expectation.note) expect(row.Payload).not.toContain(expectation.note);
  expect(row.TraceParent).toMatch(/^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/);

  const read = await readOps(id);
  expect(read.status()).toBe(200);
  expect(await read.json()).toEqual(body);
  return body;
}

async function machineToken(adminPat: string, projectId: string, orgId: string, name: string, roleKeys: string[]) {
  const post = async (path: string, body: unknown) => {
    const response = await fetch(`${authOrigin}/${path}`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${adminPat}`, 'Content-Type': 'application/json', 'Connect-Protocol-Version': '1' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(30_000),
    });
    return { status: response.status, json: (await response.json()) as Json };
  };
  const found = await post('zitadel.user.v2.UserService/ListUsers', { queries: [{ userNameQuery: { userName: name, method: 'TEXT_QUERY_METHOD_EQUALS' } }] });
  let userId = found.json.result?.[0]?.userId as string | undefined;
  if (!userId) {
    userId = (await post('zitadel.user.v2.UserService/CreateUser', { organizationId: orgId, username: name, machine: { name, accessTokenType: 'ACCESS_TOKEN_TYPE_JWT' } })).json.id as string;
  }
  if (roleKeys.length > 0) {
    await post('zitadel.authorization.v2.AuthorizationService/CreateAuthorization', { userId, projectId, organizationId: orgId, roleKeys }); // 409 when it already exists
  }
  const secret = (await post('zitadel.user.v2.UserService/AddSecret', { userId })).json.clientSecret as string;
  // A new secret can take a moment to reach the read side, so the grant is retried briefly.
  let granted: Json = {};
  for (let attempt = 0; attempt < 15 && !granted.access_token; attempt++) {
    if (attempt > 0) await new Promise((resolve) => setTimeout(resolve, 1000));
    const grant = await fetch(`${authOrigin}/oauth/v2/token`, {
      method: 'POST',
      headers: { Authorization: `Basic ${Buffer.from(`${name}:${secret}`).toString('base64')}`, 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({ grant_type: 'client_credentials', scope: `openid urn:zitadel:iam:org:project:id:${projectId}:aud urn:zitadel:iam:org:projects:roles` }),
      signal: AbortSignal.timeout(30_000),
    });
    granted = (await grant.json()) as Json;
  }
  const accessToken = granted.access_token as string;
  expect(accessToken, `token for ${name}: ${granted.error ?? ''} ${granted.error_description ?? ''}`).toBeTruthy();
  return { token: accessToken, sub: decode(accessToken).sub as string };
}

test.beforeAll(async ({ browser }) => {
  test.setTimeout(240_000);
  token = execFileSync('scripts/operations-token.sh', { encoding: 'utf8', cwd: root }).trim();
  sub = decode(token).sub;
  ops = await playwrightRequest.newContext({ baseURL: origin });
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');

  const clients = JSON.parse(execFileSync('docker', ['compose', 'run', '--rm', '--no-deps', '-T', '--entrypoint', 'cat', 'provision', '/out/zitadel-clients.json'], { encoding: 'utf8', cwd: root, stdio: ['ignore', 'pipe', 'ignore'] }));
  const adminPat = execFileSync('docker', ['run', '--rm', '-v', 'disputedesk_zitadel-bootstrap:/b', 'alpine', 'cat', '/b/admin.pat'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  const projectId = clients.Zitadel.ProjectId as string;
  const me = await fetch(`${authOrigin}/auth/v1/users/me`, { headers: { Authorization: `Bearer ${adminPat}` }, signal: AbortSignal.timeout(30_000) });
  const orgId = ((await me.json()) as Json).user.details.resourceOwner as string;
  machine = {
    noRole: await machineToken(adminPat, projectId, orgId, 'qa52-norole', []),
    readOnly: await machineToken(adminPat, projectId, orgId, 'qa52-readonly', ['dispute.read']),
    unmapped: await machineToken(adminPat, projectId, orgId, 'qa52-unmapped-allroles', ['dispute.read', 'dispute.startReview', 'dispute.escalate', 'dispute.resolve']),
  };

  // d1: submitted -> underReview -> resolved (upheld), customer A. d2: submitted -> escalated -> resolved (rejected).
  // d3: submitted -> underReview -> escalated. d4: withdrawn by its owner. d5: input and race target. d6: race against withdraw.
  for (const [key, logins] of Object.entries({ d1: ['b'], d2: ['b'], d3: ['b'], d4: ['b'], d5: ['b'], d6: ['b'] })) {
    const raised = await raise(...logins);
    ids[key] = raised.id;
    owner[key] = raised.login;
  }
});

test.afterAll(async () => {
  await ops?.dispose();
  for (const context of Object.values(contexts)) await context.close();
});

test('the machine users: one holds no role, one only dispute.read, one every role but maps to no actor', () => {
  const roles = (jwt: string) => Object.entries(decode(jwt)).filter(([key]) => key.endsWith(':roles')).flatMap(([, value]) => Object.keys(value as Json)).sort();
  expect(roles(machine.noRole.token)).toEqual([]);
  expect(roles(machine.readOnly.token)).toEqual(['dispute.read']);
  expect(roles(machine.unmapped.token)).toEqual(['dispute.escalate', 'dispute.read', 'dispute.resolve', 'dispute.startReview']);
  expect(roles(token)).toEqual(['dispute.escalate', 'dispute.read', 'dispute.resolve', 'dispute.startReview']);
});

test.describe.serial('FR-24: each action allowed only in its cells, 200 equals the read, one event each', () => {
  test('d1: submitted refuses resolve; start review -> underReview; refuses start review; resolve upheld; resolved refuses all three', async () => {
    const id = ids.d1;
    await step(id, 'resolve', { status: 409, disputeStatus: 'submitted' }, resolveBody('upheld', 'x'));
    await step(id, 'start-review', { status: 200, from: 'submitted', to: 'underReview' });
    await step(id, 'start-review', { status: 409, disputeStatus: 'underReview' });
    await step(id, 'resolve', { status: 200, from: 'underReview', to: 'resolved', outcome: 'upheld', note: "We've reversed this debit." }, resolveBody('upheld', "  We've reversed this debit.  \n"));
    for (const action of ['start-review', 'escalate'] as const) await step(id, action, { status: 409, disputeStatus: 'resolved' });
    await step(id, 'resolve', { status: 409, disputeStatus: 'resolved' }, resolveBody('rejected', 'Again.'));
    expect(events(id).map((event) => event.EventType)).toEqual(['DisputeRaised', 'ReviewStarted', 'DisputeResolved']);
    expect(disputeRow(id)).toMatchObject({ Status: expect.stringMatching(/resolved/i), Outcome: expect.stringMatching(/upheld/i), Version: 3 });
  });

  test('d2: submitted escalates; escalated refuses start review and escalate; resolve rejected (unknown properties ignored)', async () => {
    const id = ids.d2;
    await step(id, 'escalate', { status: 200, from: 'submitted', to: 'escalated' });
    await step(id, 'start-review', { status: 409, disputeStatus: 'escalated' });
    await step(id, 'escalate', { status: 409, disputeStatus: 'escalated' });
    await step(id, 'resolve', { status: 200, from: 'escalated', to: 'resolved', outcome: 'rejected', note: 'Not a bank error.' }, { json: { outcome: 'rejected', note: 'Not a bank error.', extra: 1, status: 'resolved' } });
  });

  test('d3: under review escalates', async () => {
    await step(ids.d3, 'start-review', { status: 200, from: 'submitted', to: 'underReview' });
    await step(ids.d3, 'escalate', { status: 200, from: 'underReview', to: 'escalated' });
  });

  test('d4: a withdrawn dispute refuses all three', async () => {
    const api = contexts[owner.d4].request;
    const csrf = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    expect((await api.post(`/api/v1/disputes/${ids.d4}/withdraw`, { headers: { 'X-XSRF-TOKEN': csrf }, data: { note: 'QA withdraw.' }, maxRedirects: 0 })).status()).toBe(200);
    await step(ids.d4, 'start-review', { status: 409, disputeStatus: 'withdrawn' });
    await step(ids.d4, 'escalate', { status: 409, disputeStatus: 'withdrawn' });
    await step(ids.d4, 'resolve', { status: 409, disputeStatus: 'withdrawn' }, resolveBody('upheld', 'x'));
  });

  test('start review and escalate ignore any body', async () => {
    await step(ids.d5, 'start-review', { status: 200, from: 'submitted', to: 'underReview' }, { json: { note: noteMarker, outcome: 'upheld' } });
    expect(JSON.stringify(events(ids.d5))).not.toContain(noteMarker);
    await step(ids.d6, 'start-review', { status: 200, from: 'submitted', to: 'underReview' }, { raw: 'free text', contentType: 'text/plain' });
  });
});

test.describe('events on the topic', () => {
  test('every outbox row of the acted disputes is on the topic once, keyed by the dispute ID, value byte for byte, with trace context', async () => {
    const wanted = [ids.d1, ids.d2, ids.d3, ids.d4].flatMap((id) => outbox(id).map((row) => ({ id, row })));
    let records = topicRecords();
    for (let attempt = 0; attempt < 20 && !wanted.every(({ row }) => records.some((record) => JSON.parse(record.value).id === row.Id)); attempt++) {
      await new Promise((resolve) => setTimeout(resolve, 3000));
      records = topicRecords();
    }
    for (const { id, row } of wanted) {
      const matching = records.filter((record) => JSON.parse(record.value).id === row.Id);
      expect(matching, row.Id).toHaveLength(1);
      expect(matching[0].key).toBe(id);
      expect(matching[0].value).toBe(row.Payload);
      const names = matching[0].headers.map((header) => header.key);
      expect(names).toEqual(expect.arrayContaining(['content-type', 'traceparent']));
      const traceparent = matching[0].headers.find((header) => header.key === 'traceparent')!.value;
      expect(traceparent.split('-')[1]).toBe(row.TraceParent.split('-')[1]);
    }
    const statusChanged = wanted.filter(({ row }) => row.EventType === 'disputedesk.dispute.status-changed.v1');
    expect(statusChanged.length).toBe(1 /* d1 */ * 2 + 2 /* d2 */ + 2 /* d3 */ + 1 /* d4 withdraw */);
    const all = JSON.stringify(records.filter((record) => wanted.some(({ id }) => id === record.key)));
    expect(all).not.toContain(sub);
    expect(all).not.toContain("We've reversed this debit.");
    expect(all).not.toContain('Not a bank error.');
  });
});

test.describe('the customer sees the actions', () => {
  test('d1 timeline: the bank started review, the bank resolved with outcome and trimmed note; nothing internal', async () => {
    const response = await contexts[owner.d1].request.get(`/api/v1/disputes/${ids.d1}`);
    expect(response.status()).toBe(200);
    const text = await response.text();
    const body = JSON.parse(text);
    expect(body).toMatchObject({ customerStatus: 'resolved', outcome: 'upheld' });
    expect(body.timeline).toHaveLength(3);
    expect(Object.keys(body.timeline[1]).sort()).toEqual(['action', 'actor', 'customerStatus', 'note', 'occurredAt']);
    expect(body.timeline[1]).toMatchObject({ action: 'startReview', actor: 'theBank', customerStatus: 'underReview', note: null });
    expect(body.timeline[2]).toMatchObject({ action: 'resolve', actor: 'theBank', customerStatus: 'resolved', note: "We've reversed this debit." });
    for (const forbidden of [sub, 'Operations', 'actorId', 'ActorId', 'traceId', 'operations']) expect(text).not.toContain(forbidden);
  });

  test('d2 timeline: an escalation shows as underReviewPrioritised', async () => {
    const body = await (await contexts[owner.d2].request.get(`/api/v1/disputes/${ids.d2}`)).json();
    expect(body.timeline).toHaveLength(3);
    expect(body.timeline[1]).toMatchObject({ action: 'escalate', actor: 'theBank', customerStatus: 'underReviewPrioritised', note: null });
    expect(body.timeline[2]).toMatchObject({ action: 'resolve', actor: 'theBank', customerStatus: 'resolved', note: 'Not a bank error.' });
    expect(body.outcome).toBe('rejected');
  });

  test('d3 escalated (open): detail shows underReviewPrioritised', async () => {
    const body = await (await contexts[owner.d3].request.get(`/api/v1/disputes/${ids.d3}`)).json();
    expect(body.customerStatus).toBe('underReviewPrioritised');
  });

  test('a resolved dispute cannot be withdrawn: 409 action-not-allowed with customerStatus', async () => {
    const api = contexts[owner.d1].request;
    const csrf = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    const response = await api.post(`/api/v1/disputes/${ids.d1}/withdraw`, { headers: { 'X-XSRF-TOKEN': csrf }, data: { note: 'QA withdraw.' }, maxRedirects: 0 });
    expect(response.status()).toBe(409);
    const body = await response.json();
    expect(expectProblemShape(body, 'action-not-allowed', 409)).toEqual([]);
    expect(body.customerStatus).toBe('resolved');
  });

  test("another customer's dispute is 404 for the customer, with no data", async () => {
    const stranger = owner.d1 === 'b' ? contexts.a : contexts.b;
    const response = await stranger.request.get(`/api/v1/disputes/${ids.d1}`);
    expect(response.status()).toBe(404);
    const text = await response.text();
    expect(text).not.toContain("We've reversed");
    expect(text).not.toContain(ids.d1);
  });

  test('browser: the history screen shows each status badge', async () => {
    for (const [key, label] of [['d1', 'Resolved'], ['d2', 'Resolved'], ['d3', 'Under review (prioritised)']] as const) {
      const page = await contexts[owner[key]].newPage();
      await page.goto('/disputes');
      const link = page.locator(`a[href="/disputes/${ids[key]}"]`).first();
      await expect(link, key).toBeVisible({ timeout: 30_000 });
      await expect(link).toContainText(label);
      const text = (await page.locator('body').innerText()).toLowerCase();
      expect(text).not.toContain('escalated');
      expect(text).not.toContain('operations');
      await page.close();
    }
  });
});

test.describe('404, and the order of checks', () => {
  const actions: Action[] = ['start-review', 'escalate', 'resolve'];

  test('an unknown ID is 404 not-found, equal to the customer 404 apart from traceId, and audited Rejected once per action', async () => {
    const customer = withoutTraceId(await (await contexts.a.request.get(`/api/v1/disputes/${unknownId}`)).json());
    const before = audit(unknownId).length;
    for (const action of actions) {
      const response = await act(unknownId, action, action === 'resolve' ? resolveBody('upheld', 'x') : {});
      expect(response.status(), action).toBe(404);
      const body = await response.json();
      expect(expectProblemShape(body, 'not-found', 404)).toEqual([]);
      expect(withoutTraceId(body)).toEqual(customer);
      expect(JSON.stringify(body)).not.toContain(unknownId);
    }
    expect(audit(unknownId).slice(before)).toEqual(actions.map((action) => ({ Action: auditAction[action], Result: 'Rejected', ActorType: 'Operations', ActorId: sub, CustomerId: null, DisputeId: unknownId })));
  });

  test('not-a-guid is 400 naming disputeId without echoing it, for every action and the read, and writes no row', async () => {
    const before = auditCount();
    for (const action of actions) {
      const response = await act('not-a-guid', action, action === 'resolve' ? resolveBody('upheld', 'x') : {});
      expect(response.status(), action).toBe(400);
      const body = await response.json();
      expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
      expect(JSON.stringify(body)).toContain('disputeId');
      expect(JSON.stringify(body)).not.toContain('not-a-guid');
    }
    expect(auditCount()).toBe(before);
  });

  // Binding failures: the framework's 400 before the lookup, also on the unknown ID, no field named, input never echoed.
  const bindingFailures: [string, Send][] = [
    ['malformed JSON', { raw: '{"outcome": "upheld", ' }],
    ['an array', { raw: '[]' }],
    ['the JSON null', { raw: 'null' }],
    ['no body', {}],
    ['a numeric outcome', { raw: '{"outcome": 1, "note": "x"}' }],
    ['an array outcome', { raw: '{"outcome": ["upheld"], "note": "x"}' }],
    ['a combined outcome', { raw: '{"outcome": "upheld, rejected", "note": "x"}' }],
    ['an unknown outcome name', { raw: '{"outcome": "maybe", "note": "x"}' }],
    ['a PascalCase outcome', { raw: '{"outcome": "Upheld", "note": "x"}' }],
    ['a numeric note', { raw: '{"outcome": "upheld", "note": 5}' }],
  ];
  for (const [name, send] of bindingFailures) {
    test(`resolve with ${name}: 400 validation before the lookup, on a known and an unknown ID, no row`, async () => {
      for (const id of [ids.d5, unknownId]) {
        const before = audit(id).length;
        const response = await act(id, 'resolve', send);
        expect(response.status(), id).toBe(400);
        const body = await response.json();
        expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
        expect(body.errors ?? {}).toEqual({});
        expect(JSON.stringify(body)).not.toMatch(/maybe|upheld, rejected|Upheld/);
        expect(audit(id).length).toBe(before);
      }
    });
  }

  test('resolve with a text/plain body: 415 validation, no row; with no token 401 and with a refused token 403 first', async () => {
    const before = audit(ids.d5).length;
    const response = await act(ids.d5, 'resolve', { raw: 'outcome=upheld', contentType: 'text/plain' });
    expect(response.status()).toBe(415);
    expect(expectProblemShape(await response.json(), 'validation', 415)).toEqual([]);
    expect(audit(ids.d5).length).toBe(before);
    const none = await act(ids.d5, 'resolve', { headers: {}, raw: 'x', contentType: 'text/plain' });
    expect(none.status()).toBe(401);
    expect(none.headers()['www-authenticate']).toBe('Bearer');
    const refused = await act(ids.d5, 'resolve', { headers: bearer(machine.noRole.token), raw: 'x', contentType: 'text/plain' });
    expect(refused.status()).toBe(403);
  });

  // Well-formed bodies: 404 first on the unknown ID, then 400 naming the field on a known one, before the status.
  const wellFormed: [string, Json, string][] = [
    ['no outcome', { note: 'x' }, 'outcome'],
    ['a null outcome', { outcome: null, note: 'x' }, 'outcome'],
    ['no note', { outcome: 'upheld' }, 'note'],
    ['a null note', { outcome: 'upheld', note: null }, 'note'],
    ['a whitespace-only note', { outcome: 'upheld', note: ' \t\n ' }, 'note'],
    ['a 501-unit note', { outcome: 'upheld', note: 'a'.repeat(501) }, 'note'],
    ['a 501-unit note ending in a surrogate pair straddling the limit', { outcome: 'upheld', note: `${'a'.repeat(499)}😀` }, 'note'],
  ];
  for (const [name, json, field] of wellFormed) {
    test(`resolve with ${name}: 404 on unknown, 400 naming ${field} on under review and on resolved, never 409`, async () => {
      expect((await act(unknownId, 'resolve', { json })).status()).toBe(404);
      for (const id of [ids.d5, ids.d1]) {
        const before = audit(id).length;
        const response = await act(id, 'resolve', { json });
        expect(response.status(), id).toBe(400);
        const body = await response.json();
        expect(expectProblemShape(body, 'validation', 400)).toEqual([]);
        expect(JSON.stringify(body.errors)).toContain(field);
        expect(JSON.stringify(body)).not.toContain('aaaaaaaaaa');
        expect(audit(id).length).toBe(before);
      }
    });
  }

  test('a note of exactly 500 units after trimming is accepted (boundary), stored trimmed; d6 resolves', async () => {
    const note = `${'b'.repeat(498)}😀`; // 498 + a surrogate pair = 500 UTF-16 code units
    expect(note.length).toBe(500);
    const body = await step(ids.d6, 'resolve', { status: 200, from: 'underReview', to: 'resolved', outcome: 'rejected', note }, { json: { outcome: 'rejected', note: `   ${note}   ` } });
    expect(body.timeline[body.timeline.length - 1].note).toBe(note);
  });

  test('401 before everything: no token, a malformed token, the session cookie alone and with antiforgery write no row', async () => {
    const before = auditCount();
    for (const action of actions) {
      const none = await act(ids.d5, action, { headers: {}, raw: '[]' });
      expect(none.status(), action).toBe(401);
      expect(none.headers()['www-authenticate']).toBe('Bearer');
      expect(expectProblemShape(await none.json(), 'unauthorized', 401)).toEqual([]);
      expect((await act(unknownId, action, { headers: bearer('not.a.jwt') })).status(), action).toBe(401);
      const cookie = await act(ids.d5, action, { headers: {} }, contexts.a.request);
      expect(cookie.status(), action).toBe(401);
      expect(cookie.headers()['location']).toBeUndefined();
      const csrf = (await (await contexts.a.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
      expect((await act(ids.d5, action, { headers: { 'X-XSRF-TOKEN': csrf } }, contexts.a.request)).status(), action).toBe(401);
    }
    expect(auditCount()).toBe(before);
    expect(events(ids.d5).map((event) => event.EventType)).toEqual(['DisputeRaised', 'ReviewStarted']);
  });
});

test.describe.serial('403: refused tokens, 403 before 404 and 400, each audited Denied by actor', () => {
  const actions: Action[] = ['start-review', 'escalate', 'resolve'];
  const refused = () => [['no role', machine.noRole], ['dispute.read only', machine.readOnly], ['every role, unmapped sub', machine.unmapped]] as const;

  test('every refused token gets 403 forbidden, the same body for a real and an unknown ID, with one Denied row each', async () => {
    const stateBefore = events(ids.d5).length;
    for (const [name, who] of refused()) {
      for (const action of actions) {
        const body = action === 'resolve' ? resolveBody('upheld', 'x') : {};
        const rowsBefore = [audit(ids.d5).length, audit(unknownId).length];
        const real = await act(ids.d5, action, { ...body, headers: bearer(who.token) });
        const unknown = await act(unknownId, action, { ...body, headers: bearer(who.token) });
        expect(real.status(), `${name} ${action}`).toBe(403);
        expect(unknown.status(), `${name} ${action}`).toBe(403);
        const realBody = await real.json();
        expect(expectProblemShape(realBody, 'forbidden', 403)).toEqual([]);
        expect(withoutTraceId(realBody)).toEqual(withoutTraceId(await unknown.json()));
        const expectedRow = (disputeId: string) => ({ Action: auditAction[action], Result: 'Denied', ActorType: 'Operations', ActorId: who.sub, CustomerId: null, DisputeId: disputeId });
        expect(audit(ids.d5).slice(rowsBefore[0])).toEqual([expectedRow(ids.d5)]);
        expect(audit(unknownId).slice(rowsBefore[1])).toEqual([expectedRow(unknownId)]);
      }
    }
    expect(events(ids.d5)).toHaveLength(stateBefore); // nothing changed
  });

  test('403 comes before 400 and 415: bad bodies, text/plain and not-a-guid still get 403; not-a-guid records a null DisputeId', async () => {
    for (const send of [{ raw: '{bad' }, { raw: 'x', contentType: 'text/plain' }, { raw: '[]' }]) {
      const before = auditCount();
      const response = await act(ids.d5, 'resolve', { ...send, headers: bearer(machine.noRole.token) });
      expect(response.status()).toBe(403);
      expect(auditCount()).toBe(before + 1);
    }
    for (const action of actions) {
      const response = await act('not-a-guid', action, { headers: bearer(machine.noRole.token) });
      expect(response.status(), action).toBe(403);
      const row = rows(`select "Action", "Result", "ActorId", "DisputeId" from "AuditLogEntries" where "ActorId" = '${machine.noRole.sub}' order by "OccurredAtUtc" desc, "Id" limit 1`)[0];
      expect(row).toEqual({ Action: auditAction[action], Result: 'Denied', ActorId: machine.noRole.sub, DisputeId: null });
    }
  });

  test('the read route is audited Denied for a refused token too (5.1 changed)', async () => {
    const before = audit(ids.d5).length;
    const response = await readOps(ids.d5, bearer(machine.noRole.token));
    expect(response.status()).toBe(403);
    expect(audit(ids.d5).slice(before)).toEqual([{ Action: 'DisputeRead', Result: 'Denied', ActorType: 'Operations', ActorId: machine.noRole.sub, CustomerId: null, DisputeId: ids.d5 }]);
  });

  test("the session cookie plus an unprivileged bearer: Denied records the bearer's sub, never the customer's", async () => {
    const before = audit(ids.d5).length;
    const response = await act(ids.d5, 'escalate', { headers: bearer(machine.readOnly.token) }, contexts[owner.d5].request);
    expect(response.status()).toBe(403);
    const added = audit(ids.d5).slice(before);
    expect(added).toEqual([{ Action: 'Escalate', Result: 'Denied', ActorType: 'Operations', ActorId: machine.readOnly.sub, CustomerId: null, DisputeId: ids.d5 }]);
    expect(JSON.stringify(added)).not.toMatch(/CUS-0000/);
  });

  test('customer routes are unchanged: a bearer is 401 with no challenge; an unlinked customer is 403 linking-required; neither writes a row', async () => {
    const before = auditCount();
    const bearerOnCustomerRoute = await ops.get(`/api/v1/disputes/${ids.d5}`, { headers: bearer(machine.noRole.token), maxRedirects: 0 });
    expect(bearerOnCustomerRoute.status()).toBe(401);
    expect(bearerOnCustomerRoute.headers()['www-authenticate']).toBeUndefined();
    const unlinked = await contexts.c.request.get(`/api/v1/disputes/${ids.d5}`, { maxRedirects: 0 });
    expect(unlinked.status()).toBe(403);
    expect(expectProblemShape(await unlinked.json(), 'linking-required', 403)).toEqual([]);
    expect(auditCount()).toBe(before);
  });
});

test.describe.serial('races over HTTP', () => {
  test('two resolves on one under review dispute: exactly one 200, the other 409 resolved with one Rejected row', async () => {
    const id = ids.d5;
    const beforeAudit = audit(id).filter((row) => row.Result === 'Rejected').length;
    const [first, second] = await Promise.all([act(id, 'resolve', { ...resolveBody('upheld', 'first'), unpaced: true }), act(id, 'resolve', { ...resolveBody('rejected', 'second'), unpaced: true })]);
    expect([first.status(), second.status()].sort()).toEqual([200, 409]);
    const loser = first.status() === 409 ? first : second;
    const body = await loser.json();
    expect(expectProblemShape(body, 'action-not-allowed', 409)).toEqual([]);
    expect(body.disputeStatus).toBe('resolved');
    expect(events(id).map((event) => event.EventType)).toEqual(['DisputeRaised', 'ReviewStarted', 'DisputeResolved']);
    expect(outbox(id).filter((row) => row.EventType.includes('status-changed'))).toHaveLength(2);
    expect(audit(id).filter((row) => row.Result === 'Rejected' && row.Action === 'Resolve')).toHaveLength(beforeAudit + 1);
    const winner = first.status() === 200 ? 'first' : 'second';
    expect(events(id)[2].Data.note).toBe(winner);
  });

  test('operations resolve against the customer withdraw: exactly one commits, the other is refused with the reloaded status', async () => {
    // A fresh under review dispute.
    const raised = await raise('b');
    await step(raised.id, 'start-review', { status: 200, from: 'submitted', to: 'underReview' });
    const api = contexts[raised.login].request;
    const csrf = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
    const [resolve, withdraw] = await Promise.all([
      act(raised.id, 'resolve', { ...resolveBody('upheld', 'race'), unpaced: true }),
      api.post(`/api/v1/disputes/${raised.id}/withdraw`, { headers: { 'X-XSRF-TOKEN': csrf }, data: { note: 'QA withdraw.' }, maxRedirects: 0 }),
    ]);
    expect([resolve.status(), withdraw.status()].sort()).toEqual([200, 409]);
    const types = events(raised.id).map((event) => event.EventType);
    expect(types).toHaveLength(3);
    if (resolve.status() === 200) {
      expect((await withdraw.json()).customerStatus).toBe('resolved');
      expect(types[2]).toBe('DisputeResolved');
    } else {
      expect((await resolve.json()).disputeStatus).toBe('withdrawn');
      expect(types[2]).toBe('DisputeWithdrawn');
    }
  });
});

test.describe('privacy', () => {
  test('the Api logs hold no token, no note, no operations sub, no customer ID', async () => {
    const result = execFileSync('docker', ['logs', 'disputedesk-api-1'], { encoding: 'utf8', maxBuffer: 128 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
    const logs = result + execFileSync('sh', ['-c', 'docker logs disputedesk-api-1 2>&1 >/dev/null || true'], { encoding: 'utf8', maxBuffer: 128 * 1024 * 1024 });
    for (const secret of [token, token.split('.')[2], machine.noRole.token, "We've reversed this debit.", 'Not a bank error.', noteMarker, 'CUS-000001', 'CUS-000002']) {
      expect(logs).not.toContain(secret);
    }
  });

  test('exported spans and logs (Aspire dashboard API) hold the operations spans but no note, token or operations sub', async () => {
    const dashboard = process.env.DASHBOARD_ORIGIN ?? 'http://localhost:18888';
    const spans = await (await fetch(`${dashboard}/api/telemetry/spans`, { signal: AbortSignal.timeout(30_000) })).text();
    const logs = await (await fetch(`${dashboard}/api/telemetry/logs`, { signal: AbortSignal.timeout(30_000) })).text();
    expect(spans).toContain('POST /api/v1/operations/disputes/{disputeId}/resolve');
    for (const text of [spans, logs]) {
      for (const secret of [sub, token.split('.')[2], "We've reversed this debit.", 'Not a bank error.', noteMarker, 'CUS-000001']) expect(text).not.toContain(secret);
    }
  });

  test('audit rows hold no outcome, note or customer ID anywhere', () => {
    const text = queryDatabase(`select coalesce(string_agg(t::text, ' '), '') from "AuditLogEntries" t where "ActorType" = 'Operations'`);
    expect(text).not.toMatch(/upheld|rejected_outcome|reversed this debit|Not a bank error|CUS-0000/i);
  });
});
