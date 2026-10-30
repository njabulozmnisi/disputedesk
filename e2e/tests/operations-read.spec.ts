import { execFileSync, spawnSync } from 'node:child_process';
import { expect, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase } from './support';

// Stories 5.1 and 5.5 (token side): operations tools read a dispute with a bearer token from scripts/operations-token.sh.
// Network level with the real Zitadel token; database facts through qa_readonly. A and B raise disputes through the
// customer API (the data is not reset between runs: each run raises on whichever transaction is still disputable).

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const unknownId = '01a189e2-0000-7000-8000-000000000000';
const customerNote = 'qa5x-customer-note-do-not-return';
const contexts: Record<string, BrowserContext> = {};
let token: string;
let operationsSub: string;
let disputeA: string;
let disputeB: string;
let customerAId: string;
let sinceAudit: string;

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

async function raiseOnFirstDisputable(api: APIRequestContext, accountRef: string): Promise<string> {
  const list = await api.get('/api/v1/transactions?limit=50');
  const row = ((await list.json()).items as { transactionId: string; accountRef: string; disputable: boolean }[])
    .find((item) => item.disputable && item.accountRef === accountRef);
  expect(row, `a disputable ${accountRef} transaction is left (recreate the volumes if not)`).toBeTruthy();
  const csrf = (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const response = await api.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': csrf, 'Idempotency-Key': `"qa5x-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"` },
    data: { accountRef, transactionId: row!.transactionId, reason: 'unrecognised', note: customerNote },
    maxRedirects: 0,
  });
  expect(response.status()).toBe(201);
  return response.headers().location.split('/').pop()!;
}

const bearer = (value = token) => ({ Authorization: `Bearer ${value}` });
const readOperations = (api: APIRequestContext, id: string, headers: Record<string, string> = bearer()) =>
  api.get(`/api/v1/operations/disputes/${id}`, { headers, maxRedirects: 0 });
const withoutTraceId = ({ traceId: _ignored, ...rest }: Record<string, unknown>) => rest;
const audit = (disputeId: string) =>
  queryDatabase(`select string_agg("Action" || ':' || "Result" || ':' || "ActorType" || ':' || "ActorId" || ':' || coalesce("CustomerId", 'null'), ',' order by "OccurredAtUtc") from "AuditLogEntries" where "DisputeId" = '${disputeId}'`);

test.beforeAll(async ({ browser }) => {
  token = execFileSync('scripts/operations-token.sh', { encoding: 'utf8', cwd: new URL('../..', import.meta.url).pathname }).trim();
  operationsSub = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString('utf8')).sub;
  sinceAudit = queryDatabase('select now()');
  contexts.a = await signIn(browser, 'customer-a');
  contexts.b = await signIn(browser, 'customer-b');
  customerAId = 'CUS-000001';
  disputeA = await raiseOnFirstDisputable(contexts.a.request, 'ACC-A-1');
  disputeB = await raiseOnFirstDisputable(contexts.b.request, 'ACC-B-1');
});

test.afterAll(async () => {
  for (const context of Object.values(contexts)) await context.close();
});

test('the token is a Zitadel access token for this project with the machine user as sub and the project roles', () => {
  const [header, payload] = token.split('.').slice(0, 2).map((part) => JSON.parse(Buffer.from(part, 'base64url').toString('utf8')));
  expect(header.alg).toBe('RS256');
  expect(header.typ).toBe('JWT'); // not at+jwt, so 5.1 criterion 2 leaves ValidTypes unset and the actor mapping is the backstop
  expect(payload.iss).toBe('http://auth.localhost:8080');
  expect(payload.aud).toHaveLength(1);
  const roles = payload[`urn:zitadel:iam:org:project:${payload.aud[0]}:roles`];
  expect(Object.keys(roles).sort()).toEqual(['dispute.escalate', 'dispute.read', 'dispute.resolve', 'dispute.startReview']);
  expect(payload.sub).toBe(operationsSub);
  const mapped = execFileSync('docker', ['compose', 'run', '--rm', '--no-deps', '-T', '--entrypoint', 'cat', 'provision', '/out/zitadel-clients.json'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
  expect(JSON.parse(mapped).Operations.Actors[operationsSub]).toBe('operations');
});

for (const [name, getId, account] of [['A', () => disputeA, 'ACC-A-1'], ['B', () => disputeB, 'ACC-B-1']] as const) {
  test(`reads customer ${name}'s dispute: 200, exactly the specified fields, nothing personal`, async ({ request }) => {
    const response = await readOperations(request, getId());
    expect(response.status()).toBe(200);
    const text = await response.text();
    const body = JSON.parse(text);
    expect(Object.keys(body).sort()).toEqual(
      ['createdAt', 'disputeId', 'disputedAmount', 'expectedAmount', 'outcome', 'reason', 'status', 'timeline', 'transaction'].sort());
    expect(body).toMatchObject({ disputeId: getId(), status: 'submitted', outcome: null, reason: 'unrecognised' });
    expect(body.transaction.accountRef).toBe(account);
    expect(body.timeline).toHaveLength(1);
    expect(Object.keys(body.timeline[0]).sort()).toEqual(['actorType', 'eventType', 'fromStatus', 'note', 'occurredAt', 'toStatus']);
    expect(body.timeline[0]).toMatchObject({ eventType: 'disputeRaised', actorType: 'customer', fromStatus: null, toStatus: 'submitted', note: null });
    for (const forbidden of ['CUS-0000', customerNote, operationsSub, 'actorId', 'traceId', 'customerId', 'loginSub']) {
      expect(text, `body must not hold ${forbidden}`).not.toContain(forbidden);
    }
    const stored = queryDatabase(`select "Status" from "Disputes" where "Id" = '${getId()}'`);
    expect(stored).toBe('Submitted');
  });
}

test('an unknown ID is 404 with the customer 404 body; a non-GUID is 400 naming disputeId without echoing it', async ({ request }) => {
  const unknown = await readOperations(request, unknownId);
  expect(unknown.status()).toBe(404);
  const body = await unknown.json();
  expect(expectProblemShape(body, 'not-found', 404)).toEqual([]);
  const customer = await contexts.a.request.get(`/api/v1/disputes/${unknownId}`);
  expect(withoutTraceId(body)).toEqual(withoutTraceId(await customer.json()));
  expect(JSON.stringify(body)).not.toContain(unknownId);

  const invalid = await readOperations(request, 'not-a-guid');
  expect(invalid.status()).toBe(400);
  const problem = await invalid.json();
  expect(expectProblemShape(problem, 'validation', 400)).toEqual([]);
  expect(JSON.stringify(problem)).toContain('disputeId');
  expect(JSON.stringify(problem)).not.toContain('not-a-guid');
});

test('no token is 401 with a bare Bearer challenge; a malformed or foreign token is 401 with no error_description', async ({ request }) => {
  const none = await readOperations(request, disputeA, {});
  expect(none.status()).toBe(401);
  expect(none.headers()['www-authenticate']).toBe('Bearer');
  expect(expectProblemShape(await none.json(), 'unauthorized', 401)).toEqual([]);
  for (const bad of [{ Authorization: 'Basic dXNlcjpwYXNz' }, bearer('not.a.jwt'), bearer(`${token.slice(0, -4)}AAAA`)]) {
    const response = await readOperations(request, disputeA, bad);
    expect(response.status()).toBe(401);
    expect(response.headers()['www-authenticate']).toMatch(/^Bearer/);
    expect(response.headers()['www-authenticate']).not.toContain('error_description');
  }
});

test('the session cookie alone on the operations route is 401; the bearer on customer, linking and unmatched routes is 401 with no challenge', async () => {
  const cookieOnly = await readOperations(contexts.a.request, disputeA, {});
  expect(cookieOnly.status()).toBe(401);
  expect(cookieOnly.headers()['location']).toBeUndefined();

  const plain = await (await import('@playwright/test')).request.newContext({ baseURL: origin });
  for (const path of [`/api/v1/disputes/${disputeA}`, '/api/v1/bff/antiforgery', '/api/v1/operations/unmatched']) {
    const response = await plain.get(path, { headers: bearer(), maxRedirects: 0 });
    expect(response.status(), path).toBe(401);
    expect(response.headers()['www-authenticate'], path).toBeUndefined();
    expect(response.headers()['location'], path).toBeUndefined();
  }
  await plain.dispose();
});

test('audit: DisputeRead Succeeded for each dispute and Rejected for the unknown one, machine-user sub, no customer ID, no 401 rows', async ({ request }) => {
  await readOperations(request, unknownId);
  expect(audit(disputeA)).toMatch(new RegExp(`^(DisputeRead:Succeeded:Operations:${operationsSub}:null,?)+$`));
  expect(audit(disputeB)).toMatch(new RegExp(`^(DisputeRead:Succeeded:Operations:${operationsSub}:null,?)+$`));
  expect(audit(unknownId)).toMatch(new RegExp(`^(DisputeRead:Rejected:Operations:${operationsSub}:null,?)+$`));
  // Rows for 400/401/403 paths: none of this spec's failed calls wrote an operations row without a dispute ID.
  const stray = queryDatabase(`select count(*) from "AuditLogEntries" where "ActorType" = 'Operations' and "OccurredAtUtc" >= '${sinceAudit}' and "DisputeId" is null`);
  expect(stray).toBe('0');
});

test('the Api logs hold neither the token nor a customer ID nor the customer note', () => {
  const result = spawnSync('docker', ['logs', 'disputedesk-api-1'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const logs = result.stdout + result.stderr;
  expect(logs.length).toBeGreaterThan(0);
  expect(logs).not.toContain(token);
  expect(logs).not.toContain(token.split('.')[2]);
  expect(logs).not.toContain(customerAId);
  expect(logs).not.toContain(customerNote);
});
