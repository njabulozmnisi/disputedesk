import { execFileSync, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process';
import { request as httpRequest } from 'node:http';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type APIRequestContext, type Browser, type BrowserContext } from '@playwright/test';
import { fixtureLogin, queryDatabase, topicRecords } from './support';

// Story 6.1: POST /api/v1/linking/start, through the real gateway, BFF, API, job, database (qa_readonly) and Mailpit.
// Signed in as customer C's login, the one fixture login with no link; customer A's login is linked (the 403 case).
// The bank-record details of customers A, C and D come from fixtures/local.json at run time and are never printed:
// assertions compare, they do not echo. The OTP is read from the Mailpit message only to check its shape.
// Only one unlinked login exists, so the "three logins" of criterion 1 are three starts by that login in turn, each
// waited out until its job has audited its stop (a start supersedes the login's previous challenge).
test.describe.configure({ mode: 'serial' });

const startPath = '/api/v1/linking/start';
const mailpit = process.env.MAILPIT_URL ?? `http://localhost:${process.env.DISPUTEDESK_MAILPIT_PORT ?? '8025'}`;
const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const problemType = (slug: string) => `https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/${slug}.md`;

type FixtureCustomer = { key: string; customerId: string; idNumber: string; cellphone: string; accounts: { accountNumber: string }[] };
type FixtureLogin = { key: string; userId: string };
const fixture = JSON.parse(readFileSync(fileURLToPath(new URL('../../fixtures/local.json', import.meta.url)), 'utf8'));
const customer = (key: string) => {
  const found = (fixture.customers as FixtureCustomer[]).find((entry) => entry.key === key)!;
  return { customerId: found.customerId, idNumber: found.idNumber, cellphone: found.cellphone, accountNumber: found.accounts[0].accountNumber };
};
const loginSub = (key: string) => (fixture.logins as FixtureLogin[]).find((entry) => entry.key === key)!.userId;
const customerA = customer('A');
const customerC = customer('C');
const customerD = customer('D');
const unmatchedDetails = { idNumber: customerD.idNumber, accountNumber: '1000000049' };
const cSub = loginSub('customer-c');
const aSub = loginSub('customer-a');
const sql = (statement: string) => queryDatabase(statement);

let unlinked: BrowserContext;
let linked: BrowserContext;
let anonymous: APIRequestContext;
let auditBaseline = '';
let mailBaseline = 0;
let topicBaseline = 0;
const startedAt = new Date().toISOString();

async function signIn(browser: Browser, loginKey: string): Promise<BrowserContext> {
  return signInAs(browser, fixtureLogin(loginKey));
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

async function antiforgeryToken(api: APIRequestContext): Promise<string> {
  return (await (await api.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
}

type Observed = { status: number; headers: Record<string, string>; text: string };

/** Posts a raw JSON string (so malformed bodies and JSON numbers can be sent), with the antiforgery token unless `token` is null. */
async function start(api: APIRequestContext, rawBody: string, token: string | null, extraHeaders: Record<string, string> = {}): Promise<Observed> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json', ...extraHeaders };
  if (token !== null) headers['X-XSRF-TOKEN'] = token;
  // The gateway's coarse limit_req (10 requests a second, burst 20, AD-23) answers 429 to a faster loop; pace the calls.
  await new Promise((resolve) => setTimeout(resolve, 150));
  const response = await api.post(startPath, { data: rawBody, headers, maxRedirects: 0 });
  return { status: response.status(), headers: response.headers(), text: await response.text() };
}

const details = (value: { idNumber: string; accountNumber: string }) => JSON.stringify(value);

/** The headers that must be equal between outcomes: all but the date, trace headers and cookies (criterion 1). */
function comparable(headers: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(headers).filter(([name]) => !/^(date|set-cookie)$|trace|request-id/i.test(name)));
}

const challengeCount = (sub: string) => Number(sql(`select count(*) from "LinkingChallenges" where "LoginSub" = '${sub}'`));
const currentChallenges = (sub: string) => Number(sql(`select count(*) from "LinkingChallenges" where "LoginSub" = '${sub}' and "SupersededAtUtc" is null and "ConsumedAtUtc" is null`));
const auditMark = () => sql(`select coalesce(max("OccurredAtUtc")::text, '1970-01-01') from "AuditLogEntries"`);
const auditCount = () => Number(sql('select count(*) from "AuditLogEntries"'));
const latestChallengeId = (sub: string) => sql(`select "Id" from "LinkingChallenges" where "LoginSub" = '${sub}' order by "CreatedAtUtc" desc limit 1`);

/** Audit rows after the baseline, as `Action|Result|ActorType|CustomerId|hasClientIp|ActorIsSub`, oldest first. */
function auditRowsAfter(skip: string, actorSub: string): string[] {
  return sql(`select concat_ws('|', "Action", "Result", "ActorType", coalesce("CustomerId", 'null'), ("ClientIp" is not null)::text, ("ActorId" = '${actorSub}')::text)
    from "AuditLogEntries" where "OccurredAtUtc" > '${skip}' order by "OccurredAtUtc"`).split('\n').filter(Boolean);
}

async function untilAudited(skip: string, action: string, actorSub = cSub): Promise<string[]> {
  const deadline = Date.now() + 40_000;
  while (Date.now() < deadline) {
    const rows = auditRowsAfter(skip, actorSub);
    if (rows.some((row) => row.startsWith(`${action}|`))) return rows;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`no ${action} audit row appeared after ${skip}`);
}

type MailSummary = { ID: string; Created: string; To: { Address: string }[] };
async function smsMessages(): Promise<MailSummary[]> {
  const response = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent('subject:"DisputeDesk SMS"')}&limit=500`);
  // Mailpit keeps messages across runs; only this run's count.
  const all = ((await response.json()).messages ?? []) as MailSummary[];
  return all.filter((message) => new Date(message.Created).getTime() >= new Date(startedAt).getTime());
}
const addressedTo = (message: MailSummary, cellphone: string) => message.To.some((to) => to.Address.includes(cellphone.replace('+', '')));
async function smsTo(cellphone: string): Promise<MailSummary[]> {
  return (await smsMessages()).filter((message) => addressedTo(message, cellphone));
}
async function smsText(id: string): Promise<string> {
  return ((await (await fetch(`${mailpit}/api/v1/message/${id}`)).json()).Text ?? '') as string;
}

function apiLogsSinceStart(): string {
  return execFileSync('sh', ['-c', `docker logs --since ${startedAt} disputedesk-api-1 2>&1`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
}

test.beforeAll(async ({ browser }) => {
  unlinked = await signIn(browser, 'customer-c');
  linked = await signIn(browser, 'customer-a');
  anonymous = await playwrightRequest.newContext({ baseURL: origin });
  auditBaseline = auditMark();
  mailBaseline = (await smsMessages()).length;
  topicBaseline = topicRecords().length;
});

test.afterAll(async () => {
  await unlinked?.close();
  await linked?.close();
  await anonymous?.dispose();
});

test('criterion 1, 4, 5, 7: no-match, matching D and linked A details give the identical 202 and the same writes; the job then audits each stop', async () => {
  const api = unlinked.request;
  const token = await antiforgeryToken(api);
  const outcomes = [
    { name: 'no match', body: unmatchedDetails, audit: 'LinkingNoMatch|Rejected', sms: false },
    { name: 'D', body: customerD, audit: 'LinkingOtpSent|Succeeded', sms: true },
    { name: 'A', body: customerA, audit: 'LinkingAlreadyLinked|Rejected', sms: false },
    { name: 'C', body: customerC, audit: 'LinkingSimSwapFailed|Rejected', sms: false },
  ];
  const observed: Observed[] = [];
  for (const outcome of outcomes) {
    const challengesBefore = challengeCount(cSub);
    const auditBefore = auditMark();
    const smsBefore = (await smsMessages()).length;
    const result = await start(api, details(outcome.body), token);
    observed.push(result);

    expect(result.status, outcome.name).toBe(202);
    expect(result.text, outcome.name).toBe('');
    // Same writes: exactly one new challenge, exactly one current, the previous one superseded, and the request's own
    // LinkingStart Succeeded audit with the client IP; checked straight away, before the job's rows.
    expect(challengeCount(cSub), outcome.name).toBe(challengesBefore + 1);
    expect(currentChallenges(cSub), outcome.name).toBe(1);
    const rows = await untilAudited(auditBefore, outcome.audit.split('|')[0]);
    expect(rows[0], outcome.name).toBe('LinkingStart|Succeeded|Customer|null|true|true');
    const stop = rows.find((row) => row.startsWith(outcome.audit))!;
    // CustomerId only once the details matched: not for no-match; the match audits carry it.
    expect(stop.endsWith('|true|true'), outcome.name).toBe(true);
    expect(stop.split('|')[3] === 'null', outcome.name).toBe(outcome.name === 'no match');
    // The SMS count moves only for D, and only after the job passed the SIM-swap check.
    const smsAfter = (await smsMessages()).length;
    expect(smsAfter - smsBefore, outcome.name).toBe(outcome.sms ? 1 : 0);
  }

  const reference = observed[0];
  for (const result of observed.slice(1)) {
    expect(result.status).toBe(reference.status);
    expect(comparable(result.headers)).toEqual(comparable(reference.headers));
    expect(result.text).toBe(reference.text);
  }
  expect(Object.keys(reference.headers)).not.toContain('location');
});

test('criterion 5, 8: D received exactly one SMS with a six-digit OTP; the challenge holds only its HMAC and a 5-minute lifetime from the send', async () => {
  // The previous test's D start is two challenges back (D, then A, then C superseded it); find the sent one by its columns.
  const sent = sql(`select "Id" from "LinkingChallenges" where "LoginSub" = '${cSub}' and "OtpSentAtUtc" is not null order by "CreatedAtUtc" desc limit 1`);
  expect(sent).toMatch(/^[0-9a-f-]{36}$/);
  expect(sql(`select "CustomerId" from "LinkingChallenges" where "Id" = '${sent}'`)).toBe(customerD.customerId);
  expect(sql(`select octet_length("OtpHmac") from "LinkingChallenges" where "Id" = '${sent}'`)).toBe('32');
  expect(sql(`select ("ExpiresAtUtc" - "OtpSentAtUtc") = interval '5 minutes' from "LinkingChallenges" where "Id" = '${sent}'`)).toBe('t');
  expect(sql(`select ("OtpSentAtUtc" >= "CreatedAtUtc") from "LinkingChallenges" where "Id" = '${sent}'`)).toBe('t');
  expect(sql(`select "Attempts"::text || ':' || ("ConsumedAtUtc" is null)::text from "LinkingChallenges" where "Id" = '${sent}'`)).toBe('0:true');

  const messages = await smsTo(customerD.cellphone);
  expect(messages).toHaveLength(1);
  const text = await smsText(messages[0].ID);
  const code = text.match(/\b(\d{6})\b/)?.[1];
  expect(code).toMatch(/^\d{6}$/);
  // The code is not in the row in any encoding the row holds.
  const row = sql(`select row_to_json(t)::text from "LinkingChallenges" t where "Id" = '${sent}'`);
  expect(row).not.toContain(code!);
  expect(sql(`select encode("OtpHmac", 'hex') from "LinkingChallenges" where "Id" = '${sent}'`)).not.toContain(code!);
  // No SMS reached C, A or the unmatched details' cellphones (they have none; A and C have one on record).
  expect(await smsTo(customerC.cellphone)).toHaveLength(0);
  expect(await smsTo(customerA.cellphone)).toHaveLength(0);
});

test('criterion 4: an extra cellphone field is ignored: same 202, the SMS goes only to the cellphone on the bank record', async () => {
  const api = unlinked.request;
  const token = await antiforgeryToken(api);
  const other = '+27829999999';
  const before = (await smsMessages()).length;
  const auditBefore = auditMark();
  const result = await start(api, JSON.stringify({ ...customerD, cellphone: other }), token);
  expect(result.status).toBe(202);
  expect(result.text).toBe('');
  await untilAudited(auditBefore, 'LinkingOtpSent');
  const added = (await smsMessages()).slice(0, (await smsMessages()).length - before);
  expect(added).toHaveLength(1);
  expect(addressedTo(added[0], customerD.cellphone)).toBe(true);
  expect(await smsTo(other)).toHaveLength(0);
});

test('criterion 1, 6: a second start supersedes the first: one current challenge, the earlier one stays as a record', async () => {
  const api = unlinked.request;
  const token = await antiforgeryToken(api);
  const firstId = latestChallengeId(cSub);
  const result = await start(api, details(unmatchedDetails), token);
  expect(result.status).toBe(202);
  const secondId = latestChallengeId(cSub);
  expect(secondId).not.toBe(firstId);
  expect(currentChallenges(cSub)).toBe(1);
  expect(sql(`select ("SupersededAtUtc" is not null)::text from "LinkingChallenges" where "Id" = '${firstId}'`)).toBe('true');
  // The new challenge is blank until its own job claims it (no match: it stays blank).
  const auditBefore = auditCount();
  await untilAudited(auditBaseline, 'LinkingNoMatch');
  expect(sql(`select ("OtpHmac" is null)::text || ':' || ("OtpSentAtUtc" is null)::text || ':' || ("CustomerId" is null)::text from "LinkingChallenges" where "Id" = '${secondId}'`)).toBe('true:true:true');
  expect(auditCount()).toBeGreaterThanOrEqual(auditBefore);
});

test('criterion 3: linking-route states and check order write no challenge', async () => {
  const challengesBefore = Number(sql('select count(*) from "LinkingChallenges"'));
  const body = details(customerD);

  const anon = await start(anonymous, body, null);
  expect(anon.status).toBe(401);
  expect(anon.headers['location']).toBeUndefined();
  expect(JSON.parse(anon.text)).toMatchObject({ type: problemType('unauthorized'), status: 401 });

  const bearer = await start(anonymous, body, null, { Authorization: 'Bearer not-a-real-token' });
  expect(bearer.status).toBe(401);
  expect(bearer.headers['location']).toBeUndefined();
  expect(JSON.parse(bearer.text)).toMatchObject({ type: problemType('unauthorized'), status: 401 });

  const tokenA = await antiforgeryToken(linked.request);
  const forbidden = await start(linked.request, body, tokenA);
  expect(forbidden.status).toBe(403);
  expect(JSON.parse(forbidden.text)).toMatchObject({ type: problemType('already-linked'), status: 403 });
  const forbiddenMalformed = await start(linked.request, '{"idNumber":"1","accountNumber":', tokenA);
  expect(forbiddenMalformed.status).toBe(403);
  expect(JSON.parse(forbiddenMalformed.text)).toMatchObject({ type: problemType('already-linked'), status: 403 });
  const forbiddenBadFields = await start(linked.request, details({ idNumber: '12', accountNumber: 'x' }), tokenA);
  expect(forbiddenBadFields.status).toBe(403);

  const noToken = await start(unlinked.request, body, null);
  expect(noToken.status).toBe(400);

  expect(Number(sql('select count(*) from "LinkingChallenges"'))).toBe(challengesBefore);
  expect(challengeCount(aSub)).toBe(0);
});

const luhnWrong = (() => {
  const base = customerD.idNumber;
  const last = (Number(base[12]) + 1) % 10;
  return `${base.slice(0, 12)}${last}`;
})();

const goodAccount = customerD.accountNumber;
const goodId = customerD.idNumber;
const formatCases: { name: string; body: string; fields: string[]; echoes: string[] }[] = [
  { name: 'idNumber missing', body: JSON.stringify({ accountNumber: goodAccount }), fields: ['idNumber'], echoes: [] },
  { name: 'idNumber null', body: `{"idNumber":null,"accountNumber":"${goodAccount}"}`, fields: ['idNumber'], echoes: [] },
  { name: 'idNumber empty', body: JSON.stringify({ idNumber: '', accountNumber: goodAccount }), fields: ['idNumber'], echoes: [] },
  { name: 'idNumber with a letter', body: JSON.stringify({ idNumber: `${goodId.slice(0, 12)}x`, accountNumber: goodAccount }), fields: ['idNumber'], echoes: [`${goodId.slice(0, 12)}x`] },
  { name: 'idNumber with a space', body: JSON.stringify({ idNumber: `${goodId.slice(0, 6)} ${goodId.slice(7)}`, accountNumber: goodAccount }), fields: ['idNumber'], echoes: [`${goodId.slice(0, 6)} ${goodId.slice(7)}`] },
  { name: 'idNumber one digit short', body: JSON.stringify({ idNumber: goodId.slice(0, 12), accountNumber: goodAccount }), fields: ['idNumber'], echoes: [goodId.slice(0, 12)] },
  { name: 'idNumber one digit long', body: JSON.stringify({ idNumber: `${goodId}1`, accountNumber: goodAccount }), fields: ['idNumber'], echoes: [`${goodId}1`] },
  { name: 'idNumber 10,000 characters', body: JSON.stringify({ idNumber: '7'.repeat(10_000), accountNumber: goodAccount }), fields: ['idNumber'], echoes: ['7'.repeat(50)] },
  { name: 'idNumber as a JSON number', body: `{"idNumber":${goodId},"accountNumber":"${goodAccount}"}`, fields: [], echoes: [] },
  { name: 'idNumber wrong check digit', body: JSON.stringify({ idNumber: luhnWrong, accountNumber: goodAccount }), fields: ['idNumber'], echoes: [luhnWrong] },
  { name: 'accountNumber missing', body: JSON.stringify({ idNumber: goodId }), fields: ['accountNumber'], echoes: [] },
  { name: 'accountNumber null', body: `{"idNumber":"${goodId}","accountNumber":null}`, fields: ['accountNumber'], echoes: [] },
  { name: 'accountNumber empty', body: JSON.stringify({ idNumber: goodId, accountNumber: '' }), fields: ['accountNumber'], echoes: [] },
  { name: 'accountNumber with a letter', body: JSON.stringify({ idNumber: goodId, accountNumber: '10000a0041' }), fields: ['accountNumber'], echoes: ['10000a0041'] },
  { name: 'accountNumber with a space', body: JSON.stringify({ idNumber: goodId, accountNumber: '1000 00041' }), fields: ['accountNumber'], echoes: ['1000 00041'] },
  { name: 'accountNumber one short of the minimum', body: JSON.stringify({ idNumber: goodId, accountNumber: '12345' }), fields: ['accountNumber'], echoes: ['12345'] },
  { name: 'accountNumber one over the maximum', body: JSON.stringify({ idNumber: goodId, accountNumber: '12345678901234567' }), fields: ['accountNumber'], echoes: ['12345678901234567'] },
  { name: 'accountNumber 10,000 characters', body: JSON.stringify({ idNumber: goodId, accountNumber: '8'.repeat(10_000) }), fields: ['accountNumber'], echoes: ['8'.repeat(50)] },
  { name: 'accountNumber as a JSON number', body: `{"idNumber":"${goodId}","accountNumber":${goodAccount}}`, fields: [], echoes: [] },
  { name: 'both fields bad', body: JSON.stringify({ idNumber: 'abc', accountNumber: 'def' }), fields: ['accountNumber', 'idNumber'], echoes: ['abc', 'def'] },
  { name: 'not valid JSON', body: '{"idNumber": ', fields: [], echoes: [] },
];

test('criterion 2: format errors are 400 validation naming only the failing field, never echoing it, writing nothing', async () => {
  const api = unlinked.request;
  const token = await antiforgeryToken(api);
  const challengesBefore = Number(sql('select count(*) from "LinkingChallenges"'));
  const auditBefore = auditCount();
  const smsBefore = (await smsMessages()).length;
  for (const formatCase of formatCases) {
    const result = await start(api, formatCase.body, token);
    expect(result.status, formatCase.name).toBe(400);
    const body = JSON.parse(result.text);
    expect(body.type, formatCase.name).toBe(problemType('validation'));
    expect(body.status, formatCase.name).toBe(400);
    const named = Object.keys(body.errors ?? {}).map((key) => key.replace(/^\$\./, '')).sort();
    if (formatCase.fields.length > 0) expect(named, formatCase.name).toEqual(formatCase.fields);
    for (const echo of [...formatCase.echoes, goodId, goodAccount, customerD.cellphone]) {
      expect(result.text, formatCase.name).not.toContain(echo);
    }
    expect(result.text, formatCase.name).not.toMatch(/Exception|StackTrace/);
  }
  expect(Number(sql('select count(*) from "LinkingChallenges"'))).toBe(challengesBefore);
  expect(auditCount()).toBe(auditBefore);
  expect((await smsMessages()).length).toBe(smsBefore);
});

test('criterion 7: every start audit row carries the connection address, not a forged X-Forwarded-For; sign-in rows keep it null', async () => {
  const api = unlinked.request;
  const token = await antiforgeryToken(api);
  const auditBefore = auditMark();
  const result = await start(api, details(unmatchedDetails), token, { 'X-Forwarded-For': '203.0.113.77' });
  expect(result.status).toBe(202);
  await untilAudited(auditBefore, 'LinkingNoMatch');
  expect(sql(`select count(*) from "AuditLogEntries" where "OccurredAtUtc" > '${auditBefore}' and "Action" like 'Linking%' and "ClientIp" is null`)).toBe('0');
  expect(sql(`select count(*) from "AuditLogEntries" where "OccurredAtUtc" > '${auditBefore}' and host("ClientIp") = '203.0.113.77'`)).toBe('0');
  // Entries the job wrote carry the trace ID of its own span, a 32-hex string; the request's differs from the job's.
  expect(sql(`select count(distinct "TraceId") from "AuditLogEntries" where "OccurredAtUtc" > '${auditBefore}' and "Action" like 'Linking%'`)).toBe('2');
  expect(sql(`select count(*) from "AuditLogEntries" where "Action" in ('SignIn', 'SignOut') and "ClientIp" is not null`)).toBe('0');
});

test('criterion 8: no ID number, account number, cellphone or OTP in any table, the Api logs or the topic; no event is emitted by linking', async () => {
  const smsToD = await smsTo(customerD.cellphone);
  const otps = (await Promise.all(smsToD.map((message) => smsText(message.ID)))).map((text) => text.match(/\b(\d{6})\b/)?.[1]).filter(Boolean) as string[];
  expect(otps.length).toBeGreaterThan(0);
  const secrets = [
    ...[customerA, customerC, customerD].flatMap((entry) => [entry.idNumber, entry.cellphone, entry.cellphone.replace('+', '')]),
    ...(fixture.customers as FixtureCustomer[]).flatMap((entry) => entry.accounts.map((account) => account.accountNumber)),
    ...otps,
  ];

  // Every row of every table as JSON. The bank's own record is not here: the stub keeps it in the core-banking container.
  const tables = sql(`select table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1`).split('\n').filter(Boolean);
  expect(tables).toContain('LinkingChallenges');
  for (const table of tables) {
    const dump = sql(`select coalesce(string_agg(row_to_json(t)::text, E'\\n'), '') from "${table}" t`);
    for (const secret of secrets) expect(dump.includes(secret), `${table} holds a secret`).toBe(false);
  }

  const logs = apiLogsSinceStart();
  for (const secret of secrets) expect(logs.includes(secret), 'api logs hold a secret').toBe(false);
  // The warning line for a full queue is the only linking log line allowed to hold anything; none is expected here.
  expect(logs).not.toMatch(/StartLinkingRequest\s*\{/);

  const topic = JSON.stringify(topicRecords());
  for (const secret of secrets) expect(topic.includes(secret), 'topic holds a secret').toBe(false);
  expect(topicRecords().length).toBe(topicBaseline);
  expect(mailBaseline).toBeGreaterThanOrEqual(0);
});


// ---------------------------------------------------------------------------------------------------------------------
// Story 6.5: the send cap, the parallel cap, an unexpected stop, the budget and the full queue, on the real stack.
// Runs after story 6.1's tests above, in the same serial run, on a fresh database: customer D then holds exactly the two
// delivered sends those tests made, which is the "2 sends in the last hour" state of criterion 2. The cap lasts an hour,
// so a re-run needs a fresh stack (docker compose down -v).
//
// Customer D has no login of its own, and only customer C's login is unlinked, so this block creates eight more
// throwaway logins in Zitadel (the IAM_OWNER token is read from its volume and never printed) and signs each in.
// The "held job" of the story's parallel test is held here with Postgres: a session-level advisory lock on D's key pair
// (the same key the job's transaction takes) keeps every D job waiting on its first statement.
//   Full queue and budget need a small queue and a short budget, set outside the tree:
//   E2E_LINKING_QUEUE_CAPACITY (the Api's Linking:QueueCapacity) and E2E_LINKING_BUDGET_SECONDS (Linking:Budget);
//   those two tests skip when the variables are not set.
// ---------------------------------------------------------------------------------------------------------------------
const queueCapacity = Number(process.env.E2E_LINKING_QUEUE_CAPACITY ?? 0);
const budgetSeconds = Number(process.env.E2E_LINKING_BUDGET_SECONDS ?? 0);
const jobsAtOnce = 4; // LinkingJobService.JobsAtOnce
const lockNamespace = 6005; // OtpSends.CustomerLockNamespace
const poolSize = 8;

type PoolLogin = { username: string; password: string; sub: string; context: BrowserContext; token: string };
const pool: PoolLogin[] = [];
const secondKey = `(('x' || substr(encode(sha256(convert_to('${customerD.customerId}', 'UTF8')), 'hex'), 1, 8))::bit(32)::int)`;

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
    const login = { username: `qa65.login${index}`, password: `Qa-Only-65-Login-${index}!`, sub: `qa65-login-${index}` };
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

/** Holds D's customer lock from a read-only session until released (or this process ends). */
function holdCustomerLock(): { release: () => Promise<void> } {
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
async function until(description: string, check: () => boolean | Promise<boolean>, seconds = 40): Promise<void> {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (await check()) return;
    await new Promise((resolve) => setTimeout(resolve, 300));
  }
  throw new Error(`timed out waiting for ${description}`);
}
async function untilLockHeld(): Promise<void> {
  await until('the lock holder', () => lockHolders() === 1);
}

/** A start without the pacing delay, for the parallel starts. */
async function startAt(login: PoolLogin, body: string): Promise<Observed> {
  const response = await login.context.request.post(startPath, { data: body, headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': login.token }, maxRedirects: 0 });
  return { status: response.status(), headers: response.headers(), text: await response.text() };
}

const sendsToD = () => Number(sql(`select count(*) from "LinkingChallenges" where "CustomerId" = '${customerD.customerId}' and "OtpSentAtUtc" > now() - interval '1 hour'`));
const auditSummary = (skip: string) => sql(`select "Action" || '|' || "Result" || '|' || coalesce("CustomerId", 'null') || '|' || ("ClientIp" is not null)::text || '|' || "ActorId" from "AuditLogEntries"
  where "OccurredAtUtc" > '${skip}' and "Action" like 'Linking%' order by "OccurredAtUtc"`).split('\n').filter(Boolean);
const count = (rows: string[], prefix: string) => rows.filter((row) => row.startsWith(prefix)).length;
const dPrefix = (action: string, result: string) => `${action}|${result}|${customerD.customerId}|true|`;
const tail = (rows: string[], sub: string) => rows.filter((row) => row.endsWith(`|${sub}`));

test.describe('story 6.5', () => {
  test.beforeAll(async ({ browser }) => {
    test.setTimeout(300_000);
    for (const login of await createPoolLogins()) {
      const context = await signInAs(browser, login);
      pool.push({ ...login, context, token: await antiforgeryToken(context.request) });
    }
  });

  test.afterAll(async () => {
    for (const login of pool) await login.context.close();
  });

  test('criterion 1, 2: with D at 2 delivered sends, 6 logins start in parallel while the lock is held and the SMS sender is down: exactly 1 claim (its send fails), 5 capped, and the failed claim still holds its slot', async () => {
    expect(sendsToD(), 'D starts this block with the two delivered sends of the 6.1 tests').toBe(2);
    const deliveredBefore = sql(`select count(*) from "LinkingChallenges" where "CustomerId" = '${customerD.customerId}' and "OtpHmac" is not null`);
    expect(deliveredBefore).toBe('2');
    const mark = auditMark();
    execFileSync('docker', ['stop', 'disputedesk-mailpit-1'], { encoding: 'utf8' });
    try {
      const holder = holdCustomerLock();
      await untilLockHeld();
      const results = await Promise.all(pool.slice(0, 6).map((login) => startAt(login, details(customerD))));
      for (const result of results) {
        expect(result.status).toBe(202);
        expect(result.text).toBe('');
      }
      // Every job reaches the lock (4 at once; the other 2 wait in the queue) and none has counted or claimed yet.
      await until('4 jobs waiting on the lock', () => lockWaiters() === jobsAtOnce);
      expect(sendsToD(), 'nothing is claimed while the lock is held').toBe(2);
      await holder.release();
      await until('6 job stops', () => auditSummary(mark).filter((row) => !row.startsWith('LinkingStart|')).length === 6);
    } finally {
      execFileSync('docker', ['start', 'disputedesk-mailpit-1'], { encoding: 'utf8' });
    }

    const rows = auditSummary(mark);
    expect(count(rows, 'LinkingStart|Succeeded|null|true|')).toBe(6);
    expect(count(rows, dPrefix('LinkingOtpSent', 'Failed'))).toBe(1);
    expect(count(rows, dPrefix('LinkingSendCapped', 'Rejected'))).toBe(5);
    expect(count(rows, dPrefix('LinkingOtpSent', 'Succeeded'))).toBe(0);
    // Six different logins, one stop each.
    const stopActors = rows.filter((row) => !row.startsWith('LinkingStart|')).map((row) => row.split('|')[4]);
    expect(new Set(stopActors).size).toBe(6);
    // The failed claim: stamped (holds its slot), OTP cleared (can never verify, no lifetime).
    expect(sendsToD(), '2 delivered + 1 failed claim').toBe(3);
    expect(sql(`select count(*) from "LinkingChallenges" where "CustomerId" = '${customerD.customerId}' and "OtpSentAtUtc" > now() - interval '1 hour' and "OtpHmac" is null and "ExpiresAtUtc" is null`)).toBe('1');
    expect(sql(`select count(*) from "LinkingChallenges" where "CustomerId" = '${customerD.customerId}' and "OtpHmac" is not null`)).toBe('2');
    // The five capped challenges stayed blank.
    expect(sql(`select count(*) from "LinkingChallenges" where "LoginSub" like 'qa65-login-%' and "CustomerId" is null and "OtpSentAtUtc" is null and "OtpHmac" is null and "SupersededAtUtc" is null`)).toBe('5');
    await until('mailpit', async () => (await fetch(`${mailpit}/api/v1/info`).catch(() => null))?.ok === true);
    expect((await smsTo(customerD.cellphone)).length, 'the sender was down, so no SMS exists for D').toBe(0);
  });

  test('criterion 1: a 4th start from another login, and one from customer C\'s login, get the identical 202, no SMS, and audit LinkingSendCapped with D\'s CustomerId', async () => {
    expect(sendsToD()).toBe(3);
    const mark = auditMark();
    const smsBefore = (await smsMessages()).length;
    const challengesBefore = Number(sql('select count(*) from "LinkingChallenges"'));
    const reference = await startAt(pool[6], details(unmatchedDetails));
    const fourth = await startAt(pool[7], details(customerD));
    const fromC = await start(unlinked.request, details(customerD), await antiforgeryToken(unlinked.request));
    for (const result of [reference, fourth, fromC]) {
      expect(result.status).toBe(202);
      expect(result.text).toBe('');
      expect(comparable(result.headers)).toEqual(comparable(reference.headers));
    }
    await until('3 stops', () => auditSummary(mark).filter((row) => !row.startsWith('LinkingStart|')).length === 3);
    const rows = auditSummary(mark);
    expect(rows.filter((row) => row.startsWith('LinkingNoMatch|Rejected|null|true|'))).toHaveLength(1);
    expect(tail(rows, pool[7].sub).filter((row) => row.startsWith(dPrefix('LinkingSendCapped', 'Rejected')))).toHaveLength(1);
    expect(tail(rows, cSub).filter((row) => row.startsWith(dPrefix('LinkingSendCapped', 'Rejected')))).toHaveLength(1);
    expect(count(rows, 'LinkingOtpSent'), 'no send was claimed').toBe(0);
    expect(sendsToD()).toBe(3);
    expect((await smsMessages()).length - smsBefore, 'no SMS for any of the three').toBe(0);
    expect(Number(sql('select count(*) from "LinkingChallenges"'))).toBe(challengesBefore + 3);
    expect(sql(`select count(*) from "LinkingChallenges" where "LoginSub" in ('${pool[7].sub}', '${cSub}') and "CustomerId" is not null and "SupersededAtUtc" is null`)).toBe('0');
  });

  test('criterion 3: a job that dies on an unexpected exception (its database session killed while it waits for the lock) audits LinkingStart Failed, claims nothing, logs only the type, and the next job still runs', async () => {
    const mark = auditMark();
    const logsFrom = new Date().toISOString();
    const holder = holdCustomerLock();
    await untilLockHeld();
    const result = await startAt(pool[0], details(customerD));
    expect(result.status).toBe(202);
    await until('the job waiting on the lock', () => lockWaiters() === 1);
    const killed = execFileSync('docker', ['exec', 'disputedesk-postgres-1', 'psql', '-U', 'postgres', '-d', 'disputedesk', '-At', '-c',
      `select count(*) from (select pg_terminate_backend(pid) from pg_locks where locktype = 'advisory' and not granted) killed`], { encoding: 'utf8' }).trim();
    expect(killed).toBe('1');
    await until('the Failed entry', () => auditSummary(mark).some((row) => row.startsWith(dPrefix('LinkingStart', 'Failed'))));
    await holder.release();
    const rows = auditSummary(mark);
    expect(tail(rows, pool[0].sub).filter((row) => row.startsWith(dPrefix('LinkingStart', 'Failed')))).toHaveLength(1);
    expect(count(rows, 'LinkingSendCapped')).toBe(0);
    expect(count(rows, 'LinkingOtpSent')).toBe(0);
    expect(sendsToD(), 'nothing claimed').toBe(3);
    // The log line holds the challenge ID and the exception type only.
    const challengeId = latestChallengeId(pool[0].sub);
    const logs = execFileSync('sh', ['-c', `docker logs --since ${logsFrom} disputedesk-api-1 2>&1`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    const line = logs.split('\n').find((entry) => entry.includes(challengeId) && entry.includes('stopped on an unexpected'));
    expect(line, 'the unexpected-stop log line').toBeDefined();
    expect(line).toMatch(new RegExp(`Linking challenge ${challengeId} stopped on an unexpected \\w+$`));
    for (const secret of [customerD.idNumber, customerD.cellphone, customerD.accountNumber, customerD.customerId]) expect(logs.includes(secret)).toBe(false);
    // The next job still runs: a start for D now ends as an audited cap stop.
    const next = auditMark();
    expect((await startAt(pool[1], details(customerD))).status).toBe(202);
    await untilAudited(next, 'LinkingSendCapped', pool[1].sub);
  });

  test('criterion 2: a lock wait past the budget ends as LinkingStart Failed and claims nothing', async () => {
    test.skip(budgetSeconds === 0, 'needs E2E_LINKING_BUDGET_SECONDS, the Api\'s Linking:Budget');
    test.setTimeout((budgetSeconds + 90) * 1000);
    const mark = auditMark();
    const holder = holdCustomerLock();
    await untilLockHeld();
    const startedWait = Date.now();
    expect((await startAt(pool[2], details(customerD))).status).toBe(202);
    await until('the Failed entry', () => auditSummary(mark).some((row) => row.startsWith(dPrefix('LinkingStart', 'Failed'))), budgetSeconds + 30);
    const waited = (Date.now() - startedWait) / 1000;
    await holder.release();
    expect(waited, 'the job waited for about the budget').toBeGreaterThanOrEqual(budgetSeconds - 2);
    expect(waited).toBeLessThan(budgetSeconds + 15);
    expect(count(auditSummary(mark), 'LinkingSendCapped')).toBe(0);
    expect(sendsToD()).toBe(3);
    // The budget's own path, not the unexpected-exception path: no "unexpected" log line for this challenge. (A budget
    // over 30 seconds would end at Npgsql's 30-second command timeout instead, as an unexpected NpgsqlException.)
    const challengeId = latestChallengeId(pool[2].sub);
    expect(apiLogsSinceStart()).not.toContain(`Linking challenge ${challengeId} stopped on an unexpected`);
  });

  test('criterion 4: with the queue full, a start with matching details and one with details that match nothing both get the same 429 without Retry-After, audited, and are not queued', async () => {
    test.skip(queueCapacity === 0, 'needs E2E_LINKING_QUEUE_CAPACITY, the Api\'s Linking:QueueCapacity');
    const accepted = jobsAtOnce + queueCapacity;
    expect(accepted).toBeLessThanOrEqual(pool.length - 2);
    const mark = auditMark();
    const holder = holdCustomerLock();
    await untilLockHeld();
    // 4 jobs run (waiting on the lock) and `queueCapacity` wait in the channel: all accepted.
    for (const login of pool.slice(0, accepted)) expect((await startAt(login, details(customerD))).status).toBe(202);
    await until('4 jobs waiting on the lock', () => lockWaiters() === jobsAtOnce);
    const matching = await startAt(pool[accepted], details(customerD));
    const nonMatching = await startAt(pool[accepted + 1], details(unmatchedDetails));
    for (const result of [matching, nonMatching]) {
      expect(result.status).toBe(429);
      expect(result.headers['content-type']).toMatch(/^application\/problem\+json/);
      expect(Object.keys(result.headers)).not.toContain('retry-after');
    }
    const body = (result: Observed) => ({ ...JSON.parse(result.text), traceId: undefined });
    expect(body(matching)).toEqual(body(nonMatching));
    expect(JSON.parse(matching.text)).toMatchObject({ type: problemType('rate-limited'), status: 429 });
    expect(typeof JSON.parse(matching.text).traceId).toBe('string');
    expect(matching.text).not.toMatch(/Exception|StackTrace/);
    expect(comparable(matching.headers)).toEqual(comparable(nonMatching.headers));
    // Evidence for the QA record: no cookies, no trace headers, no traceId.
    console.log('full-queue 429', JSON.stringify({ status: matching.status, headers: comparable(matching.headers), body: body(matching) }));

    const rows = auditSummary(mark);
    for (const login of [pool[accepted], pool[accepted + 1]]) {
      const own = tail(rows, login.sub);
      expect(own, login.username).toEqual([`LinkingStart|Succeeded|null|true|${login.sub}`, `LinkingRateLimited|Rejected|null|true|${login.sub}`]);
      // Saved but never queued: the blank challenge stays current and unclaimed.
      expect(sql(`select count(*) from "LinkingChallenges" where "LoginSub" = '${login.sub}' and "SupersededAtUtc" is null and "ConsumedAtUtc" is null and "CustomerId" is null and "OtpHmac" is null and "OtpSentAtUtc" is null`)).toBe('1');
    }
    await holder.release();
    // The accepted starts all run to their stop (D is at the cap) and the two refused ones never do.
    await until('the accepted jobs to stop', () => auditSummary(mark).filter((row) => row.startsWith('LinkingSendCapped|')).length === accepted, 60);
    await new Promise((resolve) => setTimeout(resolve, 2000));
    const finalRows = auditSummary(mark);
    expect(count(finalRows, 'LinkingSendCapped|')).toBe(accepted);
    expect(finalRows.filter((row) => /LinkingNoMatch/.test(row))).toHaveLength(0);
    expect(sendsToD()).toBe(3);
  });

  test('criterion 1, 3, 4: no ID number, account number, cellphone, customer ID or OTP HMAC in the Api logs, the dashboard\'s spans and logs, or the topic', async () => {
    const hmacs = sql(`select encode("OtpHmac", 'hex') from "LinkingChallenges" where "OtpHmac" is not null`).split('\n').filter(Boolean);
    expect(hmacs.length).toBeGreaterThan(0);
    const secrets = [customerD.idNumber, customerD.cellphone, customerD.cellphone.replace('+', ''), customerD.accountNumber, ...hmacs];
    const dashboard = `http://localhost:${process.env.DISPUTEDESK_DASHBOARD_PORT ?? '18888'}/api/telemetry`;
    const spans = await (await fetch(`${dashboard}/spans?limit=5000`)).text();
    const logs = await (await fetch(`${dashboard}/logs?limit=5000`)).text();
    expect(spans.length).toBeGreaterThan(1000);
    for (const [name, text] of [['api logs', apiLogsSinceStart()], ['spans', spans], ['dashboard logs', logs], ['topic', JSON.stringify(topicRecords())]] as const) {
      for (const secret of secrets) expect(text.includes(secret), `${name} holds a secret`).toBe(false);
    }
    expect(spans).toContain('linking job');
  });
});
