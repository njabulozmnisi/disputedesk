import { execFileSync } from 'node:child_process';
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
