import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type APIRequestContext, type Browser } from '@playwright/test';
import { fixtureLogin, mailpit, mailpitSearch, messageText, origin, pause, queryDatabase, registerLogin, sessionCookieName, smsTo, topicRecords, type Login } from './support';

// Story 6.2: POST /api/v1/linking/verify, through the real gateway, BFF, API, job, database (qa_readonly), Mailpit and
// the Aspire telemetry API. Customer D is the only customer a new login can link to and links once per database, so the
// journeys are ordered: every journey that must not link runs first, the race for D (and the happy path of the winner)
// runs last. Only customer C has a fixture login that is unlinked, so the other unlinked logins are registered through
// Zitadel (register page, e-mail code read from Mailpit), as a new user would. Fixture bank details and OTPs are only
// compared, never printed. Expiry waits for the real five minutes: the stack has no clock to move.
test.describe.configure({ mode: 'serial' });

const verifyPath = '/api/v1/linking/verify';
const startPath = '/api/v1/linking/start';
const dashboard = process.env.ASPIRE_URL ?? `http://localhost:${process.env.DISPUTEDESK_DASHBOARD_PORT ?? '18888'}`;
const problemType = (slug: string) => `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/${slug}.md`;

type FixtureCustomer = { key: string; customerId: string; idNumber: string; cellphone: string; accounts: { accountNumber: string }[] };
const fixture = JSON.parse(readFileSync(fileURLToPath(new URL('../../fixtures/local.json', import.meta.url)), 'utf8'));
const customer = (key: string) => {
  const found = (fixture.customers as FixtureCustomer[]).find((entry) => entry.key === key)!;
  return { customerId: found.customerId, idNumber: found.idNumber, cellphone: found.cellphone, accountNumber: found.accounts[0].accountNumber, accounts: found.accounts.map((a) => a.accountNumber) };
};
const loginSub = (key: string) => (fixture.logins as { key: string; userId: string }[]).find((entry) => entry.key === key)!.userId;
const customerA = customer('A');
const customerC = customer('C');
const customerD = customer('D');
const startDetails = (value: { idNumber: string; accountNumber: string }) => JSON.stringify({ idNumber: value.idNumber, accountNumber: value.accountNumber });
const dDetails = startDetails(customerD);
const sql = (statement: string) => queryDatabase(statement);
const startedAt = new Date().toISOString();

const logins: Record<string, Login> = {};
let anonymous: APIRequestContext;
let topicBaseline = 0;
const everyCodeSeen = new Set<string>();

// ---- sign-in and registration -------------------------------------------------------------------------------------

async function signInFixture(browser: Browser, key: string): Promise<Login> {
  const login = fixtureLogin(key);
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/api/v1/bff/login');
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  await page.close();
  return { label: key, email: login.username, context, sub: loginSub(key) };
}

// ---- requests ------------------------------------------------------------------------------------------------------

type Observed = { status: number; headers: Record<string, string>; text: string };
const antiforgeryToken = async (api: APIRequestContext) => (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;

/** Posts a raw body (so malformed JSON and JSON numbers can be sent). The gateway's limit_req is 10 a second, burst 20, so sequential calls are paced. */
async function post(api: APIRequestContext, path: string, rawBody: string | null, token: string | null, extraHeaders: Record<string, string> = {}, paced = true): Promise<Observed> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', ...extraHeaders };
  if (token !== null) headers['X-XSRF-TOKEN'] = token;
  if (paced) await pause(150);
  const response = await api.post(path, { data: rawBody ?? undefined, headers, maxRedirects: 0 });
  return { status: response.status(), headers: response.headers(), text: await response.text() };
}
const verify = (api: APIRequestContext, code: string, token: string | null, extraHeaders: Record<string, string> = {}, paced = true) =>
  post(api, verifyPath, JSON.stringify({ code }), token, extraHeaders, paced);

function problem(observed: Observed): Record<string, unknown> {
  return JSON.parse(observed.text) as Record<string, unknown>;
}
/** The body without its random trace ID, for comparing outcomes (6.2 criterion 4). */
function withoutTraceId(observed: Observed): unknown {
  const body = problem(observed);
  delete body.traceId;
  return body;
}
function comparable(headers: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(headers).filter(([name]) => !/^(date|set-cookie)$|trace|request-id/i.test(name)));
}
function expectOtpIncorrect(observed: Observed, attemptsRemaining: number, label = '') {
  expect(observed.status, label).toBe(422);
  expect(problem(observed), label).toMatchObject({ type: problemType('otp-incorrect'), status: 422, attemptsRemaining });
}
function expectLocked(observed: Observed, label = '') {
  expect(observed.status, label).toBe(409);
  expect(problem(observed), label).toMatchObject({ type: problemType('linking-locked'), status: 409 });
}

/** A six-digit code that is not `real` and not any code seen so far (so the privacy scan can tell codes apart from noise). */
let wrongCounter = 100_000 + Math.floor(Math.random() * 800_000);
function wrongCode(real: string | null): string {
  let candidate: string;
  do {
    wrongCounter = 100_000 + ((wrongCounter + 7919) % 900_000);
    candidate = String(wrongCounter);
  } while (candidate === real);
  everyCodeSeen.add(candidate);
  return candidate;
}

// ---- database and mail -----------------------------------------------------------------------------------------------

const auditCount = () => Number(sql('select count(*) from "AuditLogEntries"'));
const linkCount = () => Number(sql('select count(*) from "CustomerLinks"'));
const challengeCount = (sub: string) => Number(sql(`select count(*) from "LinkingChallenges" where "LoginSub" = '${sub}'`));
const currentChallenge = (sub: string) =>
  sql(`select concat_ws('|', "Id", "Attempts", coalesce("CustomerId", 'null'), ("OtpHmac" is not null)::text, ("ConsumedAtUtc" is not null)::text)
       from "LinkingChallenges" where "LoginSub" = '${sub}' and "SupersededAtUtc" is null and "ConsumedAtUtc" is null`);
const attemptsOf = (sub: string) => Number(currentChallenge(sub).split('|')[1]);
const auditMark = () => sql(`select coalesce(max("OccurredAtUtc")::text, '1970-01-01') from "AuditLogEntries"`);

/** Audit rows of one login after a mark, as `Action|Result|CustomerId|hasClientIp`, oldest first. */
function auditRows(sub: string, since: string): string[] {
  return sql(`select concat_ws('|', "Action", "Result", coalesce("CustomerId", 'null'), ("ClientIp" is not null)::text)
    from "AuditLogEntries" where "ActorId" = '${sub}' and "OccurredAtUtc" > '${since}' order by "OccurredAtUtc"`).split('\n').filter(Boolean);
}
async function untilAudited(sub: string, since: string, action: string): Promise<string[]> {
  const deadline = Date.now() + 40_000;
  while (Date.now() < deadline) {
    const rows = auditRows(sub, since);
    if (rows.some((row) => row.startsWith(`${action}|`))) return rows;
    await pause(500);
  }
  throw new Error(`no ${action} audit row for ${sub} after ${since}`);
}

/** Waits for the next OTP SMS to D's cellphone beyond `knownCount` and returns its code. */
async function nextOtpForD(knownCount: number): Promise<string> {
  const deadline = Date.now() + 40_000;
  while (Date.now() < deadline) {
    const messages = await smsTo(customerD.cellphone, startedAt);
    if (messages.length > knownCount) {
      // Mailpit lists newest first.
      const code = (await messageText(messages[0].ID)).match(/\b(\d{6})\b/)?.[1];
      expect(code).toMatch(/^\d{6}$/);
      everyCodeSeen.add(code!);
      return code!;
    }
    await pause(500);
  }
  throw new Error('no OTP SMS reached customer D');
}
const smsCountToD = async () => (await smsTo(customerD.cellphone, startedAt)).length;

/** Starts linking for `login` with `details` and waits for the job's stop; returns the code when D's OTP was sent. */
async function startFor(login: Login, details: string, expectedAuditAction: string): Promise<string | null> {
  const api = login.context.request;
  const token = await antiforgeryToken(api);
  const smsBefore = await smsCountToD();
  const since = auditMark();
  const started = await post(api, startPath, details, token);
  expect(started.status).toBe(202);
  await untilAudited(login.sub, since, expectedAuditAction);
  if (expectedAuditAction !== 'LinkingOtpSent') return null;
  return nextOtpForD(smsBefore);
}

function apiLogsSinceStart(): string {
  return ['disputedesk-api-1', 'disputedesk-worker-1']
    .map((container) => execFileSync('sh', ['-c', `docker logs --since ${startedAt} ${container} 2>&1`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }))
    .join('\n');
}

// ---- set-up -------------------------------------------------------------------------------------------------------------

test.beforeAll(async ({ browser }) => {
  test.setTimeout(240_000);
  logins.c = await signInFixture(browser, 'customer-c');
  logins.a = await signInFixture(browser, 'customer-a');
  for (const label of ['none', 'lock', 'expiry', 'released', 'win', 'lose']) logins[label] = await registerLogin(browser, 'qa62', label);
  anonymous = await playwrightRequest.newContext({ baseURL: origin });
  topicBaseline = topicRecords().length;
});

test.afterAll(async () => {
  for (const login of Object.values(logins)) await login.context.close();
  await anonymous?.dispose();
});

// ---- criterion 5: route states, check order and the code's bounds ---------------------------------------------------

test('criterion 5: 401, 403 already-linked, 400 antiforgery come first; the code bounds give 400 naming only code and count nothing', async () => {
  const api = logins.none.context.request;
  const token = await antiforgeryToken(api);
  const auditBefore = auditCount();
  const challengesBefore = Number(sql('select count(*) from "LinkingChallenges"'));
  const validBody = JSON.stringify({ code: '123456' });

  const anon = await post(anonymous, verifyPath, validBody, null);
  expect(anon.status).toBe(401);
  expect(anon.headers['location']).toBeUndefined();
  expect(problem(anon)).toMatchObject({ type: problemType('unauthorized'), status: 401 });
  const bearer = await post(anonymous, verifyPath, validBody, null, { Authorization: 'Bearer not-a-real-token' });
  expect(bearer.status).toBe(401);
  expect(bearer.headers['location']).toBeUndefined();

  const tokenA = await antiforgeryToken(logins.a.context.request);
  for (const body of [validBody, '{"code":', JSON.stringify({ code: 'abc' })]) {
    const forbidden = await post(logins.a.context.request, verifyPath, body, tokenA);
    expect(forbidden.status, body).toBe(403);
    expect(problem(forbidden), body).toMatchObject({ type: problemType('already-linked'), status: 403 });
  }

  const noToken = await post(api, verifyPath, validBody, null);
  expect(noToken.status).toBe(400);
  const noTokenBadBody = await post(api, verifyPath, JSON.stringify({ code: 'abc' }), null);
  expect(noTokenBadBody.status).toBe(400);

  const tooLong = '7'.repeat(10_000);
  const cases: { name: string; body: string; echo: string | null }[] = [
    { name: 'missing', body: '{}', echo: null },
    { name: 'null', body: '{"code":null}', echo: null },
    { name: 'empty', body: '{"code":""}', echo: null },
    { name: '5 digits', body: '{"code":"12345"}', echo: '12345' },
    { name: '7 digits', body: '{"code":"1234567"}', echo: '1234567' },
    { name: 'with a letter', body: '{"code":"12345a"}', echo: '12345a' },
    { name: 'leading space', body: '{"code":" 123456"}', echo: ' 123456' },
    { name: 'trailing space', body: '{"code":"123456 "}', echo: '123456 ' },
    { name: 'Arabic-Indic digits', body: '{"code":"١٢٣٤٥٦"}', echo: '١٢٣٤٥٦' },
    { name: '10,000 characters', body: JSON.stringify({ code: tooLong }), echo: '7'.repeat(40) },
    { name: 'other fields ignored, code still bad', body: '{"code":"12a456","challengeId":"x","customerId":"CUS-000004"}', echo: '12a456' },
  ];
  for (const bounds of cases) {
    const result = await post(api, verifyPath, bounds.body, token);
    expect(result.status, bounds.name).toBe(400);
    const body = problem(result);
    expect(body.type, bounds.name).toBe(problemType('validation'));
    expect(Object.keys((body.errors ?? {}) as object).map((key) => key.replace(/^\$\./, '')), bounds.name).toEqual(['code']);
    delete body.traceId;
    if (bounds.echo !== null) expect(JSON.stringify(body), bounds.name).not.toContain(bounds.echo);
  }
  // A body the framework cannot bind: 400 validation with no field named (AD-8).
  for (const unbindable of ['{"code":123456}', '{"code":', '[]']) {
    const result = await post(api, verifyPath, unbindable, token);
    expect(result.status, unbindable).toBe(400);
    expect(problem(result).type, unbindable).toBe(problemType('validation'));
    expect(problem(result).errors, unbindable).toBeUndefined();
  }
  const noBody = await api.post(verifyPath, { headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }, maxRedirects: 0 });
  expect(noBody.status()).toBe(400);
  const notJson = await api.post(verifyPath, { data: 'code=123456', headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'X-XSRF-TOKEN': token }, maxRedirects: 0 });
  expect([400, 415]).toContain(notJson.status());

  expect(auditCount()).toBe(auditBefore);
  expect(Number(sql('select count(*) from "LinkingChallenges"'))).toBe(challengesBefore);
  expect(linkCount()).toBe(2);
});

// ---- criterion 4: a login with no challenge row -----------------------------------------------------------------------

test('criterion 4: a login with no challenge row gets 422 attemptsRemaining 2 every time, never locked, nothing counted', async () => {
  const login = logins.none;
  const api = login.context.request;
  const token = await antiforgeryToken(api);
  const since = auditMark();
  const first = await verify(api, wrongCode(null), token);
  expectOtpIncorrect(first, 2);
  for (let attempt = 2; attempt <= 5; attempt++) {
    const next = await verify(api, wrongCode(null), token);
    expectOtpIncorrect(next, 2, `attempt ${attempt}`);
    expect(withoutTraceId(next)).toEqual(withoutTraceId(first));
  }
  expect(challengeCount(login.sub)).toBe(0);
  // Each answer is audited as LinkingOtpIncorrect Rejected with no customer, from a client address.
  expect(auditRows(login.sub, since)).toEqual(Array(5).fill('LinkingOtpIncorrect|Rejected|null|true'));
  expect(first.headers['set-cookie']).toBeUndefined();
});

// ---- criterion 3: three attempts, then locked --------------------------------------------------------------------------

let referenceSequence: Observed[] = [];

test('criterion 3, 8: D, wrong, wrong, wrong gives 2, 1, locked; the correct code on a fourth attempt stays locked; counted and audited', async () => {
  const login = logins.lock;
  const api = login.context.request;
  const code = (await startFor(login, dDetails, 'LinkingOtpSent'))!;
  const token = await antiforgeryToken(api);
  const since = auditMark();
  const linksBefore = linkCount();
  expect(currentChallenge(login.sub).split('|').slice(1, 4)).toEqual(['0', customerD.customerId, 'true']);

  const one = await verify(api, wrongCode(code), token);
  expectOtpIncorrect(one, 2);
  expect(attemptsOf(login.sub)).toBe(1);
  // An unrelated header does not move the stored address: no forged X-Forwarded-For (AD-23, until story 6.3).
  const two = await verify(api, wrongCode(code), token, { 'X-Forwarded-For': '203.0.113.77' });
  expectOtpIncorrect(two, 1);
  const three = await verify(api, wrongCode(code), token);
  expectLocked(three);
  expect(attemptsOf(login.sub)).toBe(3);
  const fourthCorrect = await verify(api, code, token);
  expectLocked(fourthCorrect, 'correct code after the lock');
  const fifth = await verify(api, wrongCode(code), token);
  expectLocked(fifth);

  referenceSequence = [one, two, three, fourthCorrect];
  expect(withoutTraceId(fourthCorrect)).toEqual(withoutTraceId(three));
  for (const observed of [one, two, three, fourthCorrect, fifth]) {
    expect(observed.headers['set-cookie']).toBeUndefined();
    expect(observed.headers['cache-control']).toMatch(/no-store/);
    expect(observed.headers['content-type']).toMatch(/application\/problem\+json/);
  }
  expect(attemptsOf(login.sub)).toBe(3);
  expect(linkCount()).toBe(linksBefore);
  expect(sql(`select count(*) from "CustomerLinks" where "LoginSub" = '${login.sub}'`)).toBe('0');
  expect(auditRows(login.sub, since)).toEqual([
    `LinkingOtpIncorrect|Rejected|${customerD.customerId}|true`,
    `LinkingOtpIncorrect|Rejected|${customerD.customerId}|true`,
    `LinkingLocked|Rejected|${customerD.customerId}|true`,
    `LinkingLocked|Rejected|${customerD.customerId}|true`,
    `LinkingLocked|Rejected|${customerD.customerId}|true`,
  ]);
  expect(sql(`select count(*) from "AuditLogEntries" where "ActorId" = '${login.sub}' and "OccurredAtUtc" > '${since}' and host("ClientIp") = '203.0.113.77'`)).toBe('0');
  // The stored address is the connection's, the same for every row.
  expect(sql(`select count(distinct "ClientIp") from "AuditLogEntries" where "ActorId" = '${login.sub}' and "OccurredAtUtc" > '${since}'`)).toBe('1');
});

// ---- criterion 4: every kind of challenge answers like a wrong code -------------------------------------------------

async function expectLikeWrongCode(login: Login, kind: string, expectedCustomerId: string | null) {
  const api = login.context.request;
  const token = await antiforgeryToken(api);
  const since = auditMark();
  const observed: Observed[] = [];
  for (let attempt = 1; attempt <= 4; attempt++) observed.push(await verify(api, wrongCode(null), token));
  expectOtpIncorrect(observed[0], 2, kind);
  expectOtpIncorrect(observed[1], 1, kind);
  expectLocked(observed[2], kind);
  expectLocked(observed[3], kind);
  // The same status, body (apart from traceId) and headers as customer D's challenge given wrong codes, attempt by attempt.
  for (let index = 0; index < 4; index++) {
    expect(observed[index].status, `${kind} attempt ${index + 1}`).toBe(referenceSequence[index].status);
    expect(withoutTraceId(observed[index]), `${kind} attempt ${index + 1}`).toEqual(withoutTraceId(referenceSequence[index]));
    expect(comparable(observed[index].headers), `${kind} attempt ${index + 1}`).toEqual(comparable(referenceSequence[index].headers));
  }
  expect(attemptsOf(login.sub)).toBe(3);
  const customerText = expectedCustomerId ?? 'null';
  expect(auditRows(login.sub, since), kind).toEqual([
    `LinkingOtpIncorrect|Rejected|${customerText}|true`,
    `LinkingOtpIncorrect|Rejected|${customerText}|true`,
    `LinkingLocked|Rejected|${customerText}|true`,
    `LinkingLocked|Rejected|${customerText}|true`,
  ]);
}

test('criterion 4: blank challenges (no match, customer A already linked, customer C after the failed SIM-swap check) answer like wrong codes', async () => {
  const login = logins.c;
  const stops: { kind: string; details: string; audit: string; customerId: string | null }[] = [
    { kind: 'no match', details: JSON.stringify({ idNumber: customerD.idNumber, accountNumber: '1000000049' }), audit: 'LinkingNoMatch', customerId: null },
    { kind: 'already linked (A)', details: startDetails(customerA), audit: 'LinkingAlreadyLinked', customerId: null },
    { kind: 'SIM swap failed (C)', details: startDetails(customerC), audit: 'LinkingSimSwapFailed', customerId: null },
  ];
  for (const stop of stops) {
    await startFor(login, stop.details, stop.audit);
    expect(currentChallenge(login.sub).split('|')[3], stop.kind).toBe('false');
    await expectLikeWrongCode(login, stop.kind, stop.customerId);
  }
  // A blank challenge never gets a CustomerId (the claim sets it), so its audit rows carry none.
  // C ends on linking-locked (FR-6), and still links nothing.
  expectLocked(await verify(login.context.request, wrongCode(null), await antiforgeryToken(login.context.request)));
  expect(sql(`select count(*) from "CustomerLinks" where "LoginSub" = '${login.sub}'`)).toBe('0');
});

test('criterion 4: a released challenge (the SMS send failed; OtpHmac null) answers like wrong codes', async () => {
  const login = logins.released;
  const api = login.context.request;
  const token = await antiforgeryToken(api);
  const since = auditMark();
  // Mailpit down: the job claims the challenge, the send fails, the claim is released (story 6.1).
  execFileSync('docker', ['stop', 'disputedesk-mailpit-1']);
  try {
    expect((await post(api, startPath, dDetails, token)).status).toBe(202);
    await untilAudited(login.sub, since, 'LinkingOtpSent');
  } finally {
    execFileSync('docker', ['start', 'disputedesk-mailpit-1']);
  }
  expect(auditRows(login.sub, since)).toContain(`LinkingOtpSent|Failed|${customerD.customerId}|true`);
  expect(currentChallenge(login.sub).split('|').slice(2, 4)).toEqual([customerD.customerId, 'false']);
  expect(sql(`select ("ExpiresAtUtc" is null)::text || ("OtpSentAtUtc" is null)::text from "LinkingChallenges" where "LoginSub" = '${login.sub}' and "SupersededAtUtc" is null`)).toBe('truetrue');
  for (let attempt = 0; attempt < 40; attempt++) {
    try { if ((await fetch(`${mailpit}/api/v1/info`)).ok) break; } catch { /* still starting */ }
    await pause(500);
  }
  await expectLikeWrongCode(login, 'released', customerD.customerId);
});

// ---- criterion 1/3: expiry, and a later start supersedes ---------------------------------------------------------------

let expiryCode = '';
let expiryChallengeId = '';
let supersededCode = '';

test('criterion 1: a later start supersedes: the earlier code fails like a wrong code on the current challenge', async () => {
  const login = logins.expiry;
  const api = login.context.request;
  supersededCode = (await startFor(login, dDetails, 'LinkingOtpSent'))!;
  const firstId = sql(`select "Id" from "LinkingChallenges" where "LoginSub" = '${login.sub}' and "SupersededAtUtc" is null`);
  expiryCode = (await startFor(login, dDetails, 'LinkingOtpSent'))!;
  expiryChallengeId = sql(`select "Id" from "LinkingChallenges" where "LoginSub" = '${login.sub}' and "SupersededAtUtc" is null`);
  expect(expiryChallengeId).not.toBe(firstId);
  expect(sql(`select ("SupersededAtUtc" is not null)::text from "LinkingChallenges" where "Id" = '${firstId}'`)).toBe('true');
  if (supersededCode === expiryCode) return test.info().annotations.push({ type: 'note', description: 'both OTPs equal by chance; superseded check skipped' });

  const token = await antiforgeryToken(api);
  const since = auditMark();
  const stale = await verify(api, supersededCode, token);
  expectOtpIncorrect(stale, 2);
  expect(attemptsOf(login.sub)).toBe(1);
  expect(sql(`select count(*) from "CustomerLinks" where "LoginSub" = '${login.sub}'`)).toBe('0');
  expect(auditRows(login.sub, since)).toEqual([`LinkingOtpIncorrect|Rejected|${customerD.customerId}|true`]);
  // The superseded challenge itself is untouched by the attempt.
  expect(sql(`select "Attempts" from "LinkingChallenges" where "Id" = '${firstId}'`)).toBe('0');
});

// ---- criterion 6, 7, 1, 2: the race for D, the winner's link and the session ------------------------------------------------

type SessionRow = { count: number; fingerprint: string };
const sessionRow = (sub: string): SessionRow => ({
  count: Number(sql(`select count(*) from "Sessions" where "LoginSub" = '${sub}'`)),
  fingerprint: sql(`select concat_ws('|', "Id", md5("KeyHash"::text), "LoginSub", "AbsoluteExpiresAtUtc") from "Sessions" where "LoginSub" = '${sub}'`),
});
async function sessionCookie(login: Login): Promise<string | undefined> {
  return (await login.context.cookies()).find((cookie) => cookie.name === sessionCookieName)?.value;
}

test('criteria 1, 2, 6, 7, 8: two logins and ten parallel verifications race for D: exactly one 204 and one link; the winner keeps its session and reads D', async ({ browser }) => {
  test.setTimeout(120_000);
  const contenders = [logins.win, logins.lose];
  const codes: Record<string, string> = {};
  for (const login of contenders) codes[login.label] = (await startFor(login, dDetails, 'LinkingOtpSent'))!;
  expect(codes.win).not.toBe(codes.lose);
  const tokens: Record<string, string> = {};
  const sessionsBefore: Record<string, SessionRow> = {};
  const cookiesBefore: Record<string, string | undefined> = {};
  for (const login of contenders) {
    tokens[login.label] = await antiforgeryToken(login.context.request);
    sessionsBefore[login.label] = sessionRow(login.sub);
    cookiesBefore[login.label] = await sessionCookie(login);
    expect(sessionsBefore[login.label].count).toBe(1);
  }
  // Both have used two wrong attempts, so the one that loses is on its third attempt (criterion 7/8: LinkingLocked).
  for (const login of contenders) {
    expectOtpIncorrect(await verify(login.context.request, wrongCode(codes[login.label]), tokens[login.label]), 2);
    expectOtpIncorrect(await verify(login.context.request, wrongCode(codes[login.label]), tokens[login.label]), 1);
  }
  const since = auditMark();
  await pause(1000);
  const winApi = logins.win.context.request;
  const loseApi = logins.lose.context.request;
  // 10 verifications from one login and 1 from the other, all with their correct code, at once.
  const results = await Promise.all([
    ...Array.from({ length: 10 }, () => verify(winApi, codes.win, tokens.win, {}, false)),
    verify(loseApi, codes.lose, tokens.lose, {}, false),
  ]);
  const winResults = results.slice(0, 10);
  const loseResult = results[10];
  const all = results;
  expect(all.filter((result) => result.status >= 500), 'no 5xx').toHaveLength(0);
  expect(all.filter((result) => result.status === 204), 'exactly one 204').toHaveLength(1);
  expect(linkCount()).toBe(3);
  expect(sql(`select count(*) from "CustomerLinks" where "CustomerId" = '${customerD.customerId}'`)).toBe('1');
  for (const result of all.filter((r) => r.status !== 204)) {
    expect([422, 409, 403], `status ${result.status}`).toContain(result.status);
    if (result.status === 403) expect(problem(result)).toMatchObject({ type: problemType('already-linked') });
    if (result.status === 422) expect(problem(result)).toMatchObject({ type: problemType('otp-incorrect') });
    if (result.status === 409) expect(problem(result)).toMatchObject({ type: problemType('linking-locked') });
  }
  for (const login of contenders) {
    const attempts = Number(sql(`select max("Attempts") from "LinkingChallenges" where "LoginSub" = '${login.sub}'`));
    expect(attempts, `${login.label} attempts`).toBeLessThanOrEqual(3);
  }

  const winnerIsWin = winResults.some((result) => result.status === 204) ;
  const winner = winnerIsWin ? logins.win : logins.lose;
  const loser = winnerIsWin ? logins.lose : logins.win;
  test.info().annotations.push({ type: 'winner', description: winnerIsWin ? '10-parallel login' : 'single-request login' });
  const loserResults = winnerIsWin ? [loseResult] : winResults;
  // The loser was on its third attempt: its counted attempt answers 409 linking-locked and audits LinkingLocked with D's ID.
  expect(loserResults.some((result) => result.status === 409), 'a loser answer is 409 linking-locked').toBe(true);
  const loserRows = auditRows(loser.sub, since);
  expect(loserRows).toContain(`LinkingLocked|Rejected|${customerD.customerId}|true`);
  expect(loserRows.filter((row) => row.startsWith('Linked|Succeeded'))).toHaveLength(0);
  expect(sql(`select count(*) from "CustomerLinks" where "LoginSub" = '${loser.sub}'`)).toBe('0');
  expect(sql(`select "Attempts"::text || ':' || ("ConsumedAtUtc" is null)::text from "LinkingChallenges" where "LoginSub" = '${loser.sub}' and "SupersededAtUtc" is null`)).toBe('3:true');

  // The winner: one Linked Succeeded audit with D's ID, the link row, the consumed challenge, no Set-Cookie on any answer.
  const winnerResponse = (winnerIsWin ? winResults : [loseResult]).find((result) => result.status === 204)!;
  expect(winnerResponse.text).toBe('');
  expect(winnerResponse.headers['set-cookie']).toBeUndefined();
  expect(winnerResponse.headers['cache-control']).toMatch(/no-store/);
  expect(all.every((result) => result.headers['set-cookie'] === undefined)).toBe(true);
  expect(auditRows(winner.sub, since).filter((row) => row === `Linked|Succeeded|${customerD.customerId}|true`)).toHaveLength(1);
  expect(sql(`select "CustomerId" from "CustomerLinks" where "LoginSub" = '${winner.sub}'`)).toBe(customerD.customerId);
  expect(sql(`select count(*) from "LinkingChallenges" where "LoginSub" = '${winner.sub}' and "ConsumedAtUtc" is not null`)).toBe('1');

  // Criterion 2: the same session cookie, the same Sessions row; the next request reads the linked customer.
  expect(sessionRow(winner.sub)).toEqual(sessionsBefore[winner.label]);
  expect(await sessionCookie(winner)).toBe(cookiesBefore[winner.label]);
  const winnerApi = winner.context.request;
  const transactions = await winnerApi.get('/api/v1/transactions?limit=50');
  expect(transactions.status()).toBe(200);
  const page = await transactions.json() as { items: { accountLast4: string }[] };
  expect(page.items.length).toBeGreaterThan(0);
  const last4 = customerD.accounts.map((account) => account.slice(-4));
  for (const row of page.items) expect(last4).toContain(row.accountLast4);
  const afterToken = await antiforgeryToken(winnerApi);
  for (const [path, body] of [[startPath, dDetails], [verifyPath, JSON.stringify({ code: wrongCode(null) })]] as const) {
    const refused = await post(winnerApi, path, body, afterToken);
    expect(refused.status, path).toBe(403);
    expect(problem(refused), path).toMatchObject({ type: problemType('already-linked') });
  }
  expect(sessionRow(winner.sub)).toEqual(sessionsBefore[winner.label]);
  // The loser is still unlinked: a customer endpoint refuses it.
  expect((await loser.context.request.get('/api/v1/transactions')).status()).toBe(403);

  // Browser hop: the winner reaches Transactions in the SPA with no new sign-in.
  const spa = await winner.context.newPage();
  await spa.goto('/');
  await expect(spa.getByRole('heading', { name: /transactions/i }).first()).toBeVisible({ timeout: 30_000 });
  expect(new URL(spa.url()).origin).toBe(origin);
  expect(await sessionCookie(winner)).toBe(cookiesBefore[winner.label]);

  // Criterion 2: logout after linking still sends id_token_hint.
  const logoutToken = await antiforgeryToken(winnerApi);
  const navigations: string[] = [];
  spa.on('request', (request) => request.isNavigationRequest() && navigations.push(request.url()));
  await spa.evaluate((token) => {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/api/v1/bff/logout';
    form.innerHTML = `<input type="hidden" name="__RequestVerificationToken" value="${token}">`;
    document.body.appendChild(form);
    form.submit();
  }, logoutToken);
  await expect.poll(() => navigations.some((url) => url.includes('/oidc/v1/end_session'))).toBe(true);
  const endSession = new URL(navigations.find((url) => url.includes('/oidc/v1/end_session'))!);
  expect(endSession.searchParams.get('id_token_hint')).toBeTruthy();
  await spa.waitForURL((url) => url.origin === origin && url.pathname === '/');
  await spa.close();
  void browser;
});

// ---- criterion 3, 8: an expired claimed challenge counts the attempt and answers the same way ------------------------------

test('criterion 3, 8: an expired claimed challenge counts the attempt, the correct code included: 422 then 409 linking-locked', async () => {
  test.setTimeout(420_000);
  const login = logins.expiry;
  const api = login.context.request;
  const expiresAt = sql(`select extract(epoch from "ExpiresAtUtc") from "LinkingChallenges" where "Id" = '${expiryChallengeId}'`);
  const sentAt = sql(`select extract(epoch from "OtpSentAtUtc") from "LinkingChallenges" where "Id" = '${expiryChallengeId}'`);
  expect(Number(expiresAt) - Number(sentAt)).toBeCloseTo(300, 2);

  // One microsecond before the stored expiry is accepted (integration test); here only the real wait: the stack's clock is the host's.
  while (Date.now() / 1000 < Number(expiresAt) + 1) await pause(2000);
  const token = await antiforgeryToken(api);
  const since = auditMark();
  const expired = await verify(api, expiryCode, token);
  expectOtpIncorrect(expired, 1);
  expect(attemptsOf(login.sub)).toBe(2);
  expect(sql(`select count(*) from "CustomerLinks" where "LoginSub" = '${login.sub}'`)).toBe('0');
  expect(auditRows(login.sub, since)).toEqual([`LinkingOtpExpired|Rejected|${customerD.customerId}|true`]);
  // The third attempt locks it, the correct code included.
  expectLocked(await verify(api, expiryCode, token));
  expectLocked(await verify(api, expiryCode, token));
  expect(auditRows(login.sub, since).slice(1)).toEqual([
    `LinkingLocked|Rejected|${customerD.customerId}|true`,
    `LinkingLocked|Rejected|${customerD.customerId}|true`,
  ]);
  void supersededCode;
});

// ---- criterion 8: nothing personal or secret anywhere ---------------------------------------------------------------------

test('criterion 8: no OTP, HMAC or ID number in any table column but OtpHmac, the Api and worker logs, the Aspire logs and spans, or the topic', async () => {
  const hmacs = sql(`select encode("OtpHmac", 'hex') from "LinkingChallenges" where "OtpHmac" is not null`).split('\n').filter(Boolean);
  expect(hmacs.length).toBeGreaterThan(3);
  const hmacForms = hmacs.flatMap((hex) => [hex, hex.toUpperCase(), Buffer.from(hex, 'hex').toString('base64'), Buffer.from(hex, 'hex').toString('base64url')]);
  // The codes this run submitted or was sent, plus every OTP SMS Mailpit still holds (a run of this test alone has only those).
  for (const message of await mailpitSearch('subject:"DisputeDesk SMS"')) {
    const code = (await messageText(message.ID)).match(/\b(\d{6})\b/)?.[1];
    if (code) everyCodeSeen.add(code);
  }
  const otpCodes = [...everyCodeSeen];
  expect(otpCodes.length).toBeGreaterThan(3);
  const personal = [
    ...[customerA, customerC, customerD].flatMap((entry) => [entry.idNumber, entry.cellphone, entry.cellphone.replace('+', '')]),
    ...(fixture.customers as FixtureCustomer[]).flatMap((entry) => entry.accounts.map((account) => account.accountNumber)),
  ];
  // A 6-digit code counts as present only as a whole token, so a digit run inside an ID or a timestamp is not a hit.
  const wholeToken = (haystack: string, token: string) => new RegExp(`(?<![0-9A-Za-z])${token}(?![0-9A-Za-z])`).test(haystack);
  const scan = (where: string, text: string) => {
    for (const hmac of hmacForms) expect(text.includes(hmac), `${where} holds an HMAC`).toBe(false);
    for (const secret of personal) expect(text.includes(secret), `${where} holds personal data`).toBe(false);
    for (const code of otpCodes) expect(wholeToken(text, code), `${where} holds an OTP`).toBe(false);
  };

  const tables = sql(`select table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1`).split('\n').filter(Boolean);
  for (const table of tables) {
    // OtpHmac is the one column allowed to hold the HMAC.
    const projection = table === 'LinkingChallenges' ? `(to_jsonb(t) - 'OtpHmac')::text` : 'to_jsonb(t)::text';
    scan(`table ${table}`, sql(`select coalesce(string_agg(${projection}, E'\\n'), '') from "${table}" t`));
  }
  // The code column holds the HMAC only, and 32 bytes.
  expect(sql(`select count(*) from "LinkingChallenges" where "OtpHmac" is not null and octet_length("OtpHmac") <> 32`)).toBe('0');

  scan('Api and worker logs', apiLogsSinceStart());
  const logs = await (await fetch(`${dashboard}/api/telemetry/logs`)).text();
  scan('Aspire logs', logs);
  const spans = await (await fetch(`${dashboard}/api/telemetry/spans`)).text();
  scan('Aspire spans', spans);
  // The dashboard keeps a bounded buffer of spans, so by now the verify spans may have aged out; what remains is scanned.
  scan('topic', JSON.stringify(topicRecords()));
  expect(topicRecords().length, 'linking emits no event').toBe(topicBaseline);
  scan('OtpHmac-less audit text', sql(`select coalesce(string_agg(to_jsonb(t)::text, E'\\n'), '') from "AuditLogEntries" t`));
});
