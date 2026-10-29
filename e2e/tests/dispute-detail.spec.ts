import { execFileSync } from 'node:child_process';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase } from './support';

// Story 3.3: a customer reads one dispute and its timeline. Network level (the detail screen is story 3.7), signed in
// through the hosted Zitadel login as the seeded customers. A and B are linked, C is not. Database facts are read through
// qa_readonly. The data is not reset between runs: each run raises on whichever transaction is still disputable.
// There is no outbox relay before story 4.1, so the events hop is the Pending outbox row and an empty topic.

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const contexts: Record<string, BrowserContext> = {};
const unknownId = '01a189e2-0000-7000-8000-000000000000';

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
  expect(row, `a disputable ${accountRef} transaction is left (recreate the volumes if not; routed to 7.1/7.3)`).toBeTruthy();
  return raise(api, { accountRef, transactionId: row!.transactionId, reason: 'unrecognised', note: 'qa 3.3' });
}

async function raise(api: APIRequestContext, body: object) {
  const token = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  return api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"qa33-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: body,
    maxRedirects: 0,
  });
}

const auditCount = () => Number(queryDatabase('select count(*) from "AuditLogEntries"'));
const noSessionValue = (headers: Record<string, string>) => {
  const { date: _date, 'set-cookie': cookie, traceparent: _t, ...rest } = headers;
  const attributes = cookie === undefined ? '' : cookie.replace(/^[^=]+=[^;]*/, '');
  return JSON.stringify({ rest: Object.entries(rest).sort(), attributes });
};
const withoutTraceId = ({ traceId: _ignored, ...rest }: Record<string, unknown>) => rest;

let own: { path: string; id: string; text: string };
let otherId: string;

test.beforeAll(async ({ browser }) => {
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  contexts.c = await signIn(browser, 'customer-c');
  const raised = await raiseOnFirstDisputable(contexts.a.request, 'ACC-A-1');
  expect(raised.status()).toBe(201);
  const path = raised.headers().location;
  own = { path, id: path.split('/').pop()!, text: await raised.text() };
  const raisedByB = await raiseOnFirstDisputable(contexts.b.request, 'ACC-B-1');
  expect(raisedByB.status()).toBe(201);
  otherId = raisedByB.headers().location.split('/').pop()!;
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

test('the raise Location serves the detail: 200, byte-equal to the 201, no-store, outcome null, stored events behind it', async () => {
  expect(own.path).toBe(`/api/v1/disputes/${own.id}`);
  const response = await contexts.a.request.get(own.path);
  expect(response.status()).toBe(200);
  expect(response.headers()['cache-control']).toBe('no-store');
  expect(response.headers()['content-type']).toContain('application/json');
  expect(await response.text()).toBe(own.text);
  const body = JSON.parse(own.text);
  expect(body.outcome).toBeNull();
  expect(Object.keys(body).sort()).toEqual(
    ['createdAt', 'customerStatus', 'disputeId', 'disputedAmount', 'expectedAmount', 'note', 'outcome', 'reason', 'timeline', 'transaction'].sort());
  expect(body.timeline).toHaveLength(1);
  expect(Object.keys(body.timeline[0]).sort()).toEqual(['action', 'actor', 'customerStatus', 'note', 'occurredAt']);
  expect(body.timeline[0]).toMatchObject({ action: 'raise', customerStatus: 'submitted', actor: 'you' });

  const stored = queryDatabase(`select string_agg("Version" || ':' || "EventType" || ':' || "ActorType", ',' order by "Version") from "StoredEvents" where "DisputeId" = '${own.id}'`);
  expect(stored).toBe('1:DisputeRaised:Customer');
  expect(queryDatabase(`select "Status" || ':' || coalesce("Outcome"::text, 'null') from "Disputes" where "Id" = '${own.id}'`)).toBe('Submitted:null');
  expect(queryDatabase(`select "EventType" || ':' || "Status" from "OutboxMessages" where "DisputeId" = '${own.id}'`)).toBe('disputedesk.dispute.raised.v1:Pending');
  // The customer wire view holds no internal value.
  const wire = own.text;
  expect(wire).not.toContain('CUS-0000');
  expect(wire).not.toContain('worker:');
  expect(wire).not.toContain('DisputeRaised');
});

test("another customer's dispute and an unknown one get the same 404, echoing nothing", async () => {
  const other = await contexts.a.request.get(`/api/v1/disputes/${otherId}`);
  const unknown = await contexts.a.request.get(`/api/v1/disputes/${unknownId}`);
  expect(other.status()).toBe(404);
  expect(unknown.status()).toBe(404);
  expect(noSessionValue(other.headers())).toBe(noSessionValue(unknown.headers()));
  const otherBody = await other.json();
  expect(expectProblemShape(otherBody, 'not-found', 404)).toEqual([]);
  expect(withoutTraceId(otherBody)).toEqual(withoutTraceId(await unknown.json()));
  expect(JSON.stringify(otherBody)).not.toContain(otherId);
});

test("raising on another customer's transaction and on an unknown one get the same 404", async () => {
  const body = (transactionId: string) => ({ accountRef: 'ACC-B-1', transactionId, reason: 'unrecognised' });
  const other = await raise(contexts.a.request, body('TXN-B-0001'));
  const unknown = await raise(contexts.a.request, body('TXN-ZZZ-9999'));
  expect(other.status()).toBe(404);
  expect(unknown.status()).toBe(404);
  expect(noSessionValue(other.headers())).toBe(noSessionValue(unknown.headers()));
  expect(withoutTraceId(await other.json())).toEqual(withoutTraceId(await unknown.json()));
});

test('authorization states: 401, then 403, then 400; bearer alone is 401; no read writes an audit row', async ({ playwright }) => {
  const before = auditCount();
  const anonymous = await playwright.request.newContext({ baseURL: origin });
  const anonymousResponse = await anonymous.get(`/api/v1/disputes/${unknownId}`, { maxRedirects: 0 });
  expect(anonymousResponse.status()).toBe(401);
  expect(anonymousResponse.headers()['www-authenticate']).toBeUndefined();
  expect(expectProblemShape(await anonymousResponse.json(), 'unauthorized', 401)).toEqual([]);
  expect((await anonymous.get('/api/v1/disputes/not-a-guid', { maxRedirects: 0 })).status()).toBe(401);
  const bearer = await anonymous.get(`/api/v1/disputes/${unknownId}`, { headers: { Authorization: 'Bearer a.b.c' }, maxRedirects: 0 });
  expect(bearer.status()).toBe(401);
  await anonymous.dispose();

  const unlinked = await contexts.c.request.get(`/api/v1/disputes/${unknownId}`);
  expect(unlinked.status()).toBe(403);
  expect((await unlinked.json()).type).toMatch(/linking-required\.md$/);
  expect((await contexts.c.request.get('/api/v1/disputes/not-a-guid')).status()).toBe(403);

  const malformed = await contexts.a.request.get('/api/v1/disputes/not-a-guid');
  expect(malformed.status()).toBe(400);
  const malformedBody = await malformed.json();
  expect(expectProblemShape(malformedBody, 'validation', 400)).toEqual([]);
  expect(JSON.stringify(malformedBody)).not.toContain('not-a-guid');

  expect(auditCount()).toBe(before);
});

test('the detail answers 200 from the stored snapshot while core banking is down', async () => {
  execFileSync('docker', ['stop', 'disputedesk-core-banking-1']);
  try {
    const response = await contexts.a.request.get(own.path);
    expect(response.status()).toBe(200);
    expect(await response.text()).toBe(own.text);
  } finally {
    execFileSync('docker', ['start', 'disputedesk-core-banking-1']);
    await expect.poll(() => execFileSync('docker', ['inspect', '-f', '{{.State.Health.Status}}', 'disputedesk-core-banking-1'], { encoding: 'utf8' }).trim(),
      { timeout: 60_000 }).toBe('healthy');
  }
});
