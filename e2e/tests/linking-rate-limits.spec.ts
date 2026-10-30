import { execFileSync, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { request as httpRequest } from 'node:http';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { queryDatabase, topicRecords } from './support';

// Story 6.3: the linking rate limits (5 requests per login, 20 per client IP, a 15-minute window), through the real
// gateway, BFF, Api, job, database (qa_readonly) and Mailpit. The limits are held in memory on the one Api instance, so
// each test restarts the Api container for a clean window; sessions live in the database and survive the restart.
// Every request through the gateway reaches the Api from one client address, which is the IP partition.
//
// Needs the Api running with the production limits (the defaults), which the other linking specs cannot run under: they
// send more than 5 requests from one login and more than 20 from one address. So:
//   E2E_LINKING_RATE_LIMITS=production   the Api runs with Linking:RateLimits at their defaults (5, 20, 15 minutes)
//   E2E_LINKING_QUEUE_CAPACITY           the Api's Linking:QueueCapacity, for the full-queue tests (as in linking-start.spec.ts)
// Run it alone on a fresh database, before the other linking specs: the full-queue tests send OTPs to customer D.
// Only the 400 responses of malformed requests are used to spend permits (a 400 takes a permit and writes nothing).
test.describe.configure({ mode: 'serial' });
test.skip(process.env.E2E_LINKING_RATE_LIMITS !== 'production', 'needs E2E_LINKING_RATE_LIMITS=production: the Api at the default limits');

const startPath = '/api/v1/linking/start';
const verifyPath = '/api/v1/linking/verify';
const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const mailpit = process.env.MAILPIT_URL ?? `http://localhost:${process.env.DISPUTEDESK_MAILPIT_PORT ?? '8025'}`;
const problemType = (slug: string) => `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/${slug}.md`;
const queueCapacity = Number(process.env.E2E_LINKING_QUEUE_CAPACITY ?? 0);
const jobsAtOnce = 4; // LinkingJobService.JobsAtOnce
const lockNamespace = 6005; // OtpSends.CustomerLockNamespace
const poolSize = 9;
const permitsPerLogin = 5;
const permitsPerIp = 20;
const sql = (statement: string) => queryDatabase(statement);
const pause = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

type FixtureCustomer = { key: string; customerId: string; idNumber: string; cellphone: string; accounts: { accountNumber: string }[] };
const fixture = JSON.parse(readFileSync(fileURLToPath(new URL('../../fixtures/local.json', import.meta.url)), 'utf8'));
const customerD = (() => {
  const found = (fixture.customers as FixtureCustomer[]).find((entry) => entry.key === 'D')!;
  return { customerId: found.customerId, idNumber: found.idNumber, cellphone: found.cellphone, accountNumber: found.accounts[0].accountNumber };
})();
const dDetails = JSON.stringify({ idNumber: customerD.idNumber, accountNumber: customerD.accountNumber });
const noMatchDetails = JSON.stringify({ idNumber: customerD.idNumber, accountNumber: '1000000049' });
const linkedLogin = fixtureLoginOf('customer-a');

function fixtureLoginOf(key: string): { username: string; password: string } {
  const login = (fixture.logins as { key: string; username: string; password: string }[]).find((entry) => entry.key === key)!;
  return { username: login.username, password: login.password };
}

// ---- logins ----------------------------------------------------------------------------------------------------------

type PoolLogin = { username: string; password: string; sub: string; context: BrowserContext; token: string };
const pool: PoolLogin[] = [];
let anonymous: APIRequestContext;
let linkedContext: BrowserContext;

function zitadel(path: string, token: string, body: unknown, method = 'POST'): Promise<{ status: number; text: string }> {
  return new Promise((resolve, reject) => {
    const payload = body === undefined ? '' : JSON.stringify(body);
    const call = httpRequest(
      { host: '127.0.0.1', port: 8080, path, method, timeout: 30_000,
        headers: { Host: 'auth.localhost:8080', Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', 'Connect-Protocol-Version': '1', 'Content-Length': Buffer.byteLength(payload) } },
      (response) => {
        let text = '';
        response.on('data', (chunk) => (text += chunk));
        response.on('end', () => resolve({ status: response.statusCode ?? 0, text }));
      });
    call.on('error', reject);
    call.end(payload);
  });
}

/** Creates the throwaway logins (email verified, fixed user IDs) with the provisioner's token, which is read from the volume and never printed. */
async function createPoolLogins(): Promise<{ username: string; password: string; sub: string }[]> {
  const image = execFileSync('docker', ['inspect', 'disputedesk-postgres-1', '--format', '{{.Config.Image}}'], { encoding: 'utf8' }).trim();
  const adminToken = execFileSync('docker', ['run', '--rm', '--entrypoint', 'cat', '-v', 'disputedesk_zitadel-bootstrap:/b:ro', image, '/b/admin.pat'], { encoding: 'utf8' }).trim();
  const me = await zitadel('/auth/v1/users/me', adminToken, undefined, 'GET');
  const organizationId = (JSON.parse(me.text) as { user: { details: { resourceOwner: string } } }).user.details.resourceOwner;
  const logins = [];
  for (let index = 1; index <= poolSize; index++) {
    const login = { username: `qa63.login${index}`, password: `Qa-Only-63-Login-${index}!`, sub: `qa63-login-${index}` };
    const created = await zitadel('/zitadel.user.v2.UserService/CreateUser', adminToken, {
      organizationId, userId: login.sub, username: login.username,
      human: { profile: { givenName: 'Qa', familyName: `Login${index}`, preferredLanguage: 'en' }, email: { email: `${login.username}@disputedesk.test`, isVerified: true }, password: { password: login.password, changeRequired: false } },
    });
    // A re-run on the same stack finds the user: Zitadel answers 400 AlreadyExisting.
    expect(created.status === 200 || created.text.includes('AlreadyExisting'), `creating ${login.username}`).toBe(true);
    logins.push(login);
  }
  return logins;
}

async function signInAs(browser: Browser, login: { username: string; password: string }): Promise<BrowserContext> {
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

const antiforgeryToken = async (api: APIRequestContext) => (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;

// ---- requests ------------------------------------------------------------------------------------------------------

type Observed = { status: number; headers: Record<string, string>; text: string };

/** Posts a raw JSON body as `login`. The gateway's coarse limit_req (10 a second, burst 20) answers faster loops with its own 429, so calls are paced. */
async function post(login: PoolLogin, path: string, rawBody: string): Promise<Observed> {
  await pause(150);
  const response = await login.context.request.post(path, {
    data: rawBody, headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': login.token }, maxRedirects: 0,
  });
  return { status: response.status(), headers: response.headers(), text: await response.text() };
}

/** Alternates start and verify with malformed bodies: both answer 400 when they take a permit, and write nothing. */
const malformed = [
  { path: verifyPath, body: JSON.stringify({ code: '12345' }) },
  { path: startPath, body: '{}' },
  { path: '/api/v1/Linking/Start', body: '{}' }, // an upper-case segment is the same route
  { path: verifyPath, body: JSON.stringify({ code: 'abcdef' }) },
  { path: startPath, body: JSON.stringify({ idNumber: 'x' }) },
  { path: verifyPath, body: '{}' },
  { path: startPath, body: '{}' },
];
const spend = (login: PoolLogin, index: number) => post(login, malformed[index % malformed.length].path, malformed[index % malformed.length].body);

async function spendPermits(login: PoolLogin, count: number): Promise<Observed[]> {
  const results: Observed[] = [];
  for (let index = 0; index < count; index++) results.push(await spend(login, index));
  return results;
}

const body = (observed: Observed) => JSON.parse(observed.text) as Record<string, unknown>;
const withoutTraceId = (observed: Observed) => ({ ...body(observed), traceId: undefined });

function expectRateLimited(observed: Observed, label: string) {
  expect(observed.status, label).toBe(429);
  expect(observed.headers['content-type'], label).toMatch(/^application\/problem\+json/);
  expect(observed.headers['retry-after'], `${label}: Retry-After`).toBe('900');
  expect(observed.headers['cache-control'], label).toBe('no-store');
  expect(Object.keys(body(observed)).sort(), label).toEqual(['detail', 'status', 'title', 'traceId', 'type']);
  expect(body(observed), label).toMatchObject({ type: problemType('rate-limited'), title: 'Too Many Requests', status: 429 });
  expect(typeof body(observed).traceId).toBe('string');
}

function expectNotRateLimited(observed: Observed, label: string) {
  expect(observed.status, label).toBe(400);
  expect(Object.keys(observed.headers), label).not.toContain('retry-after');
}

/** Restarts the Api for a clean limiter window and waits until the gateway serves it again. */
async function restartApi(): Promise<void> {
  execFileSync('docker', ['restart', 'disputedesk-api-1'], { encoding: 'utf8' });
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    const health = execFileSync('docker', ['inspect', 'disputedesk-api-1', '--format', '{{.State.Health.Status}}'], { encoding: 'utf8' }).trim();
    if (health === 'healthy') {
      const response = await anonymous.get('/api/v1/bff/session').catch(() => null);
      if (response && response.status() === 200) break;
    }
    await pause(500);
  }
  for (const login of pool) login.token = await antiforgeryToken(login.context.request);
}

// ---- database ----------------------------------------------------------------------------------------------------------

const auditMark = () => sql(`select coalesce(max("OccurredAtUtc")::text, '1970-01-01') from "AuditLogEntries"`);
/** `Action|Result|CustomerId|hasClientIp` of the login's audit rows after the mark, oldest first. */
const auditRows = (sub: string, since: string) => sql(`select concat_ws('|', "Action", "Result", coalesce("CustomerId", 'null'), ("ClientIp" is not null)::text)
  from "AuditLogEntries" where "ActorId" = '${sub}' and "OccurredAtUtc" > '${since}' order by "OccurredAtUtc"`).split('\n').filter(Boolean);
const rateLimitedRows = (sub: string, since: string) => auditRows(sub, since).filter((row) => row.startsWith('LinkingRateLimited|'));

async function until(description: string, check: () => boolean | Promise<boolean>, seconds = 40): Promise<void> {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (await check()) return;
    await pause(300);
  }
  throw new Error(`timed out waiting for ${description}`);
}

// ---- setup ---------------------------------------------------------------------------------------------------------------

test.beforeAll(async ({ browser }) => {
  test.setTimeout(300_000);
  anonymous = await playwrightRequest.newContext({ baseURL: origin });
  for (const login of await createPoolLogins()) {
    const context = await signInAs(browser, login);
    pool.push({ ...login, context, token: await antiforgeryToken(context.request) });
  }
  linkedContext = await signInAs(browser, linkedLogin);
});

test.afterAll(async () => {
  for (const login of pool) await login.context.close();
  await linkedContext?.close();
  await anonymous?.dispose();
  // Leave the next spec a clean window.
  if (process.env.E2E_LINKING_RATE_LIMITS === 'production') execFileSync('docker', ['restart', 'disputedesk-api-1'], { encoding: 'utf8' });
});

// ---- criteria 1, 2, 3: one login ---------------------------------------------------------------------------------------

test('criteria 1, 2, 3: a login gets 5 requests, the 6th and 7th are 429 with Retry-After 900 and one generic body, and only the first is audited', async () => {
  await restartApi();
  const login = pool[0];
  const mark = auditMark();
  const permitted = await spendPermits(login, permitsPerLogin);
  permitted.forEach((observed, index) => expectNotRateLimited(observed, `request ${index + 1}`));

  const sixth = await post(login, verifyPath, JSON.stringify({ code: '12345' }));
  const seventh = await post(login, startPath, '{}');
  expectRateLimited(sixth, 'verify 6th');
  expectRateLimited(seventh, 'start 7th');
  // One fixed generic detail, the same for both routes and both rejections, never the login, address or a count.
  expect(withoutTraceId(sixth)).toEqual(withoutTraceId(seventh));
  for (const secret of [login.sub, login.username, '10.201.']) {
    expect(sixth.text.includes(secret), `the 429 body holds ${secret}`).toBe(false);
  }
  expect(String(body(sixth).detail), 'the detail holds a count').not.toMatch(/\d/);

  // Audit: exactly one LinkingRateLimited Rejected for the two rejections, with the address and no customer.
  expect(rateLimitedRows(login.sub, mark)).toEqual(['LinkingRateLimited|Rejected|null|true']);
  expect(sql(`select "ActorType" || '|' || host("ClientIp") from "AuditLogEntries" where "ActorId" = '${login.sub}' and "Action" = 'LinkingRateLimited' and "OccurredAtUtc" > '${mark}'`)).toMatch(/^Customer\|\d+\.\d+\.\d+\.\d+$/);
  expect(sql(`select count(*) from "AuditLogEntries" where "ActorId" = '${login.sub}' and "Action" like 'LinkingStart%' and "OccurredAtUtc" > '${mark}'`)).toBe('0');
});

/** 25 unauthenticated and 25 already-linked requests: they would empty the IP limit (20) if they took permits. */
async function refusedBeforeTheLimiter(): Promise<void> {
  for (let index = 0; index < 25; index++) {
    const response = await anonymous.post(index % 2 === 0 ? startPath : verifyPath, { data: '{}', headers: { 'Content-Type': 'application/json' }, maxRedirects: 0 });
    expect(response.status(), 'anonymous').toBe(401);
    expect(response.headers()['retry-after']).toBeUndefined();
    await pause(150);
  }
  const linkedToken = await antiforgeryToken(linkedContext.request);
  for (let index = 0; index < 25; index++) {
    const response = await linkedContext.request.post(index % 2 === 0 ? startPath : verifyPath, {
      data: index % 2 === 0 ? '{}' : JSON.stringify({ code: '123456' }), headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': linkedToken }, maxRedirects: 0,
    });
    expect(response.status(), 'linked login').toBe(403);
    expect(response.headers()['retry-after']).toBeUndefined();
    await pause(150);
  }
}

test('criterion 1: 401 and 403 take no permit from the login; a 400 does; the mixed-case path counts', async () => {
  await restartApi();
  await refusedBeforeTheLimiter();
  // The unlinked login still has all five of its permits (the third request is the mixed-case path), and the sixth is refused.
  const results = await spendPermits(pool[1], permitsPerLogin);
  results.forEach((observed, index) => expectNotRateLimited(observed, `request ${index + 1} after 50 refused`));
  expectRateLimited(await post(pool[1], startPath, '{}'), '6th');
});

// ---- criterion 1: the IP limit and the chained edge ----------------------------------------------------------------------

test('criterion 1: after 50 refused 401 and 403, four logins at their limit use the 20 IP permits, and the 5th login\'s request is 429 and audited; later ones are not', async () => {
  await restartApi();
  await refusedBeforeTheLimiter();
  const mark = auditMark();
  for (const login of pool.slice(0, 4)) {
    const results = await spendPermits(login, permitsPerLogin);
    results.forEach((observed, index) => expectNotRateLimited(observed, `${login.sub} request ${index + 1}`));
  }
  const fifth = pool[4];
  const twentyFirst = await spend(fifth, 0);
  expectRateLimited(twentyFirst, 'the 21st request from the address');
  const twentySecond = await spend(fifth, 1);
  expectRateLimited(twentySecond, 'the 22nd request');
  expect(withoutTraceId(twentyFirst)).toEqual(withoutTraceId(twentySecond));
  // The address was rejected for the first time here, so the login is audited once; the fifth login's own limit was never reached.
  expect(rateLimitedRows(fifth.sub, mark)).toEqual(['LinkingRateLimited|Rejected|null|true']);
  for (const login of pool.slice(0, 4)) expect(rateLimitedRows(login.sub, mark), login.sub).toEqual([]);
});

test('criterion 1: a request the login limit rejects has already spent two IP permits, so the address runs out 2 early', async () => {
  await restartApi();
  const mark = auditMark();
  // Login 1 uses 5 permits and has its 6th request rejected by its own limit: 5 + 2 = 7 IP permits gone.
  (await spendPermits(pool[0], permitsPerLogin)).forEach((observed, index) => expectNotRateLimited(observed, `login 1 request ${index + 1}`));
  expectRateLimited(await spend(pool[0], 0), 'login 1, 6th (its own limit)');
  expect(rateLimitedRows(pool[0].sub, mark)).toHaveLength(1);

  // 13 permits are left for the address: logins 2 and 3 take 5 each, login 4 takes 3, and its 4th is the address's rejection.
  for (const login of [pool[1], pool[2]]) {
    (await spendPermits(login, permitsPerLogin)).forEach((observed, index) => expectNotRateLimited(observed, `${login.sub} request ${index + 1}`));
  }
  (await spendPermits(pool[3], 3)).forEach((observed, index) => expectNotRateLimited(observed, `login 4 request ${index + 1}`));
  expectRateLimited(await spend(pool[3], 3), 'login 4, 4th (the address\'s limit)');
  // A new login rejected from an address that was already rejected is audited too: its own entry was absent.
  expect(rateLimitedRows(pool[3].sub, mark)).toEqual(['LinkingRateLimited|Rejected|null|true']);
  // Had the rejected request spent one IP permit, not two, login 4 would have had a 4th request.
});

// ---- criterion 4 and the claim: the full queue and a locked challenge -----------------------------------------------------

/** Holds D's customer lock from a read-only session until released (or this process ends). */
function holdCustomerLock(): { release: () => Promise<void> } {
  const secondKey = `(('x' || substr(encode(sha256(convert_to('${customerD.customerId}', 'UTF8')), 'hex'), 1, 8))::bit(32)::int)`;
  const child: ChildProcessWithoutNullStreams = spawn('docker',
    ['exec', '-i', '-e', `PGPASSWORD=${process.env.QA_DB_PASSWORD ?? 'dev-only-qa-db'}`, 'disputedesk-postgres-1', 'psql', '-h', 'localhost', '-U', 'qa_readonly', '-d', 'disputedesk', '-At'],
    { stdio: 'pipe' });
  child.stdin.write(`select pg_advisory_lock(${lockNamespace}, ${secondKey});\n`);
  return {
    release: () => new Promise((resolve) => {
      child.on('close', () => resolve());
      child.stdin.end(); // ends the session, which releases the lock
    }),
  };
}
const lockWaiters = () => Number(sql(`select count(*) from pg_locks where locktype = 'advisory' and not granted`));
const lockHolders = () => Number(sql(`select count(*) from pg_locks where locktype = 'advisory' and granted and classid = ${lockNamespace}`));
const smsToD = async () => {
  const response = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent('subject:"DisputeDesk SMS"')}&limit=500`);
  const messages = ((await response.json()).messages ?? []) as { To: { Address: string }[] }[];
  return messages.filter((message) => message.To.some((to) => to.Address.includes(customerD.cellphone.replace('+', '')))).length;
};
const challengeOf = (sub: string) => sql(`select concat_ws('|', "Attempts", coalesce("CustomerId", 'null'), ("OtpHmac" is not null)::text, ("OtpSentAtUtc" is not null)::text)
  from "LinkingChallenges" where "LoginSub" = '${sub}' and "SupersededAtUtc" is null and "ConsumedAtUtc" is null`);
const wrongCodes = ['111111', '222222', '333333'];

test('claim: a challenge locked by three wrong codes while its job waits gets no SMS, and the job audits a rejected send', async () => {
  test.skip(queueCapacity === 0, 'needs E2E_LINKING_QUEUE_CAPACITY, the Api\'s Linking:QueueCapacity');
  await restartApi();
  const login = pool[0];
  const smsBefore = await smsToD();
  const mark = auditMark();
  const holder = holdCustomerLock();
  await until('the lock holder', () => lockHolders() === 1);
  try {
    expect((await post(login, startPath, dDetails)).status).toBe(202);
    await until('the job waiting on the lock', () => lockWaiters() === 1);
    // The start left a blank current challenge; wrong codes lock it like any other.
    expect(challengeOf(login.sub)).toBe('0|null|false|false');
    const first = await post(login, verifyPath, JSON.stringify({ code: wrongCodes[0] }));
    const second = await post(login, verifyPath, JSON.stringify({ code: wrongCodes[1] }));
    const third = await post(login, verifyPath, JSON.stringify({ code: wrongCodes[2] }));
    expect([first.status, second.status, third.status]).toEqual([422, 422, 409]);
    expect(body(first)).toMatchObject({ attemptsRemaining: 2 });
    expect(body(second)).toMatchObject({ attemptsRemaining: 1 });
    expect(body(third)).toMatchObject({ type: problemType('linking-locked') });
  } finally {
    await holder.release();
  }
  await until('the job to stop', () => auditRows(login.sub, mark).some((row) => row.startsWith('LinkingOtpSent|')));
  await pause(2000);
  const rows = auditRows(login.sub, mark);
  expect(rows.filter((row) => row.startsWith('LinkingOtpSent|'))).toEqual([`LinkingOtpSent|Rejected|${customerD.customerId}|true`]);
  expect(rows.some((row) => row.startsWith('LinkingOtpSent|Succeeded'))).toBe(false);
  // Nothing was claimed: no OTP in the row, no send stamped, no SMS to D.
  expect(challengeOf(login.sub)).toBe('3|null|false|false');
  expect(await smsToD()).toBe(smsBefore);
});

test('criterion 4: with the queue full, the blank challenge of a refused start answers three wrong codes like customer D\'s delivered challenge', async () => {
  test.skip(queueCapacity === 0, 'needs E2E_LINKING_QUEUE_CAPACITY, the Api\'s Linking:QueueCapacity');
  await restartApi();
  const accepted = pool.slice(1, 1 + jobsAtOnce + queueCapacity);
  const refused = [pool[pool.length - 2], pool[pool.length - 1]];
  expect(accepted.length + refused.length).toBeLessThanOrEqual(pool.length - 1);
  const mark = auditMark();
  const holder = holdCustomerLock();
  await until('the lock holder', () => lockHolders() === 1);
  try {
    for (const login of accepted) expect((await post(login, startPath, dDetails)).status, login.sub).toBe(202);
    await until('4 jobs waiting on the lock', () => lockWaiters() === jobsAtOnce);
    const refusedWithMatch = await post(refused[0], startPath, dDetails);
    const refusedNoMatch = await post(refused[1], startPath, noMatchDetails);
    for (const observed of [refusedWithMatch, refusedNoMatch]) {
      expect(observed.status).toBe(429);
      expect(Object.keys(observed.headers)).not.toContain('retry-after'); // the full-queue 429 keeps none
    }
  } finally {
    await holder.release();
  }
  // The accepted jobs run: D's three sends are delivered to three logins, the rest are capped (a blank challenge too).
  await until('the accepted jobs to stop', () => accepted.every((login) => auditRows(login.sub, mark).some((row) => /^Linking(OtpSent|SendCapped)\|/.test(row))), 60);
  const delivered = accepted.filter((login) => challengeOf(login.sub).split('|')[2] === 'true');
  expect(delivered.length).toBeGreaterThan(0);

  // Reference: D's delivered challenge given three wrong codes.
  const reference: Observed[] = [];
  for (const code of wrongCodes) reference.push(await post(delivered[0], verifyPath, JSON.stringify({ code })));
  expect(reference.map((observed) => observed.status)).toEqual([422, 422, 409]);
  expect(reference.slice(0, 2).map((observed) => body(observed).attemptsRemaining)).toEqual([2, 1]);

  // The blank each refused start left (current, unclaimed, no OTP) answers attempt by attempt like the reference.
  for (const login of refused) {
    expect(challengeOf(login.sub), login.sub).toBe('0|null|false|false');
    for (let attempt = 0; attempt < wrongCodes.length; attempt++) {
      const observed = await post(login, verifyPath, JSON.stringify({ code: wrongCodes[attempt] }));
      expect(observed.status, `${login.sub} attempt ${attempt + 1}`).toBe(reference[attempt].status);
      expect(withoutTraceId(observed), `${login.sub} attempt ${attempt + 1}`).toEqual(withoutTraceId(reference[attempt]));
      expect(observed.headers['content-type']).toBe(reference[attempt].headers['content-type']);
    }
  }
});

test('no personal data in the Api logs, the dashboard spans and logs, or the topic: no ID number, cellphone, account number, OTP HMAC, partition key', async () => {
  const hmacs = sql(`select encode("OtpHmac", 'hex') from "LinkingChallenges" where "OtpHmac" is not null`).split('\n').filter(Boolean);
  const secrets = [customerD.idNumber, customerD.cellphone, customerD.cellphone.replace('+', ''), customerD.accountNumber, ...hmacs];
  const dashboard = `http://localhost:${process.env.DISPUTEDESK_DASHBOARD_PORT ?? '18888'}/api/telemetry`;
  const spans = await (await fetch(`${dashboard}/spans?limit=5000`)).text();
  const logs = await (await fetch(`${dashboard}/logs?limit=5000`)).text();
  const apiLogs = execFileSync('sh', ['-c', 'docker logs disputedesk-api-1 2>&1'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  expect(spans.length).toBeGreaterThan(1000);
  for (const [name, text] of [['api logs', apiLogs], ['spans', spans], ['dashboard logs', logs], ['topic', JSON.stringify(topicRecords())]] as const) {
    for (const secret of secrets) expect(text.includes(secret), `${name} holds a secret`).toBe(false);
    // The rate-limit partition keys are the login's sub and the client address: neither is logged by the limiter.
    for (const login of pool) expect(text.includes(login.sub), `${name} holds ${login.sub}`).toBe(false);
  }
});
