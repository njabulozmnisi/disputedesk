import AxeBuilder from '@axe-core/playwright';
import { execFileSync, spawn, type ChildProcessWithoutNullStreams } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { request as httpRequest } from 'node:http';
import { fileURLToPath } from 'node:url';
import { expect, test, type Browser, type BrowserContext, type Page, type Response } from '@playwright/test';
import { queryDatabase, topicRecords } from './support';

// Story 6.4: the linking screens 2a (/link) and 2b (/link/otp) in a real browser, through the real gateway, BFF, Api,
// job, database (qa_readonly), Mailpit (the SMS sink) and the Aspire telemetry API. Customer D is the only customer a
// new login can link to and links once per database, so the journeys are ordered: the lock journey, the rate-limited
// journeys, then the real link last. The limits are the production ones (5 requests per login, 20 per client address,
// 15 minutes), held in memory, so each journey restarts the Api for a clean window (sessions live in the database).
// Fixture bank details and OTPs are only compared and typed, never printed.
//
// Run on a fresh database, alone: it takes customer D's one link. The full-queue journey (429 with no Retry-After)
// needs the Api started with Linking:QueueCapacity=1 (E2E_LINKING_QUEUE_CAPACITY=1) and runs last.
test.describe.configure({ mode: 'serial' });

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const mailpit = process.env.MAILPIT_URL ?? `http://localhost:${process.env.DISPUTEDESK_MAILPIT_PORT ?? '8025'}`;
const dashboard = process.env.ASPIRE_URL ?? `http://localhost:${process.env.DISPUTEDESK_DASHBOARD_PORT ?? '18888'}`;
const queueCapacity = Number(process.env.E2E_LINKING_QUEUE_CAPACITY ?? 0);
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const startedAt = new Date().toISOString();
const sql = (statement: string) => queryDatabase(statement);
const pause = (milliseconds: number) => new Promise((resolve) => setTimeout(resolve, milliseconds));

type FixtureCustomer = { key: string; customerId: string; idNumber: string; cellphone: string; accounts: { accountNumber: string }[] };
const fixture = JSON.parse(readFileSync(fileURLToPath(new URL('../../fixtures/local.json', import.meta.url)), 'utf8'));
const dFixture = (fixture.customers as FixtureCustomer[]).find((entry) => entry.key === 'D')!;
const customerD = { customerId: dFixture.customerId, idNumber: dFixture.idNumber, cellphone: dFixture.cellphone, accountNumber: dFixture.accounts[0].accountNumber };
const noMatchAccount = '1000000049'; // a valid-format account number that no customer holds
const wrongCodes = ['111111', '222222', '333333'];

// ---- throwaway logins (as linking-rate-limits.spec.ts creates them) -----------------------------------------------------

type Login = { username: string; password: string; sub: string };
const logins: Login[] = [];
const loginCount = 14;
const everyCodeSeen = new Set<string>(wrongCodes);
const allRequestUrls: string[] = [];
const evidence: string[] = [];
const note = (line: string) => {
  evidence.push(line);
  console.log(`EVIDENCE ${line}`);
};

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

async function createLogins(): Promise<Login[]> {
  const image = execFileSync('docker', ['inspect', 'disputedesk-postgres-1', '--format', '{{.Config.Image}}'], { encoding: 'utf8' }).trim();
  const adminToken = execFileSync('docker', ['run', '--rm', '--entrypoint', 'cat', '-v', 'disputedesk_zitadel-bootstrap:/b:ro', image, '/b/admin.pat'], { encoding: 'utf8' }).trim();
  const me = await zitadel('/auth/v1/users/me', adminToken, undefined, 'GET');
  const organizationId = (JSON.parse(me.text) as { user: { details: { resourceOwner: string } } }).user.details.resourceOwner;
  const created: Login[] = [];
  for (let index = 1; index <= loginCount; index++) {
    const login = { username: `qa64.login${index}`, password: `Qa-Only-64-Login-${index}!`, sub: `qa64-login-${index}` };
    const result = await zitadel('/zitadel.user.v2.UserService/CreateUser', adminToken, {
      organizationId, userId: login.sub, username: login.username,
      human: { profile: { givenName: 'Qa', familyName: `Login${index}`, preferredLanguage: 'en' }, email: { email: `${login.username}@disputedesk.test`, isVerified: true }, password: { password: login.password, changeRequired: false } },
    });
    expect(result.status === 200 || result.text.includes('AlreadyExisting'), `creating ${login.username}`).toBe(true);
    created.push(login);
  }
  return created;
}

async function signIn(browser: Browser, login: Login): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext();
  const page = await context.newPage();
  page.on('request', (request) => allRequestUrls.push(request.url()));
  await page.goto('/api/v1/bff/login');
  await page.getByTestId('username-text-input').fill(login.username);
  await page.getByTestId('username-text-input').press('Enter');
  await page.getByTestId('password-text-input').fill(login.password, { timeout: 60_000 });
  await page.getByTestId('password-text-input').press('Enter');
  await page.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  return { context, page };
}

async function restartApi(): Promise<void> {
  execFileSync('docker', ['restart', 'disputedesk-api-1'], { encoding: 'utf8' });
  const deadline = Date.now() + 90_000;
  while (Date.now() < deadline) {
    const health = execFileSync('docker', ['inspect', 'disputedesk-api-1', '--format', '{{.State.Health.Status}}'], { encoding: 'utf8' }).trim();
    if (health === 'healthy') {
      const response = await fetch(`${origin}/api/v1/bff/session`).catch(() => null);
      if (response?.status === 200) return;
    }
    await pause(500);
  }
  throw new Error('the Api did not come back');
}

// ---- recording the linking calls of one page -------------------------------------------------------------------------------

type Call = { path: string; status: number; retryAfter: string | null; contentType: string | null; body: string | null; xsrf: boolean; query: string };
function recordLinking(page: Page): Call[] {
  const calls: Call[] = [];
  page.on('response', (response: Response) => {
    const url = new URL(response.url());
    if (!url.pathname.startsWith('/api/v1/linking/')) return;
    const request = response.request();
    calls.push({
      path: url.pathname, status: response.status(), retryAfter: response.headers()['retry-after'] ?? null,
      contentType: response.headers()['content-type'] ?? null, body: request.postData(), xsrf: 'x-xsrf-token' in request.headers(), query: url.search,
    });
  });
  return calls;
}

// ---- the four hops ------------------------------------------------------------------------------------------------------------

const auditRows = (sub: string) => sql(`select concat_ws('|', "Action", "Result", coalesce("CustomerId", 'null'))
  from "AuditLogEntries" where "ActorId" = '${sub}' and "OccurredAtUtc" > '${startedAt}' order by "OccurredAtUtc"`).split('\n').filter(Boolean);
const challengeOf = (sub: string) => sql(`select concat_ws('|', "Attempts", coalesce("CustomerId", 'null'), ("OtpHmac" is not null)::text, ("OtpSentAtUtc" is not null)::text, ("ConsumedAtUtc" is not null)::text, ("SupersededAtUtc" is not null)::text)
  from "LinkingChallenges" where "LoginSub" = '${sub}' order by "CreatedAtUtc" desc limit 1`);
const linkRow = (sub: string) => sql(`select "CustomerId" from "CustomerLinks" where "LoginSub" = '${sub}'`);

async function until(description: string, check: () => boolean | Promise<boolean>, seconds = 40): Promise<void> {
  const deadline = Date.now() + seconds * 1000;
  while (Date.now() < deadline) {
    if (await check()) return;
    await pause(300);
  }
  throw new Error(`timed out waiting for ${description}`);
}

type MailSummary = { ID: string; Created: string; To: { Address: string }[] };
async function smsToD(): Promise<MailSummary[]> {
  const response = await fetch(`${mailpit}/api/v1/search?query=${encodeURIComponent('subject:"DisputeDesk SMS"')}&limit=500`);
  const messages = ((await response.json()).messages ?? []) as MailSummary[];
  return messages.filter((message) => message.To.some((to) => to.Address.includes(customerD.cellphone.replace('+', ''))));
}
async function nextCodeForD(knownCount: number): Promise<string> {
  let code: string | undefined;
  await until('the OTP SMS to D', async () => {
    const messages = await smsToD();
    if (messages.length <= knownCount) return false;
    const text = ((await (await fetch(`${mailpit}/api/v1/message/${messages[0].ID}`)).json()).Text ?? '') as string;
    code = text.match(/\b(\d{6})\b/)?.[1];
    return true;
  });
  expect(code).toMatch(/^\d{6}$/);
  everyCodeSeen.add(code!);
  return code!;
}

// ---- page helpers ---------------------------------------------------------------------------------------------------------------

const idNumber = (page: Page) => page.getByLabel('ID number');
const accountNumber = (page: Page) => page.getByLabel('Account number');
const code = (page: Page) => page.getByLabel('6-digit one-time code');

async function submitDetails(page: Page, id: string, account: string) {
  await idNumber(page).fill(id);
  await accountNumber(page).fill(account);
  await page.getByRole('button', { name: 'Continue' }).click();
}

/** What a typed value must never reach: the URL, history entries, storage, the page text and every network URL. */
async function expectNothingKept(page: Page, context: BrowserContext, typed: string[], label: string) {
  const state = await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length, href: location.href, hash: location.hash, state: JSON.stringify(history.state) }));
  expect([state.local, state.session], `${label}: storages`).toEqual([0, 0]);
  expect(state.hash, `${label}: hash`).toBe('');
  const cdp = await context.newCDPSession(page);
  const history = (await cdp.send('Page.getNavigationHistory')) as { entries: { url: string }[] };
  const text = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  for (const value of typed) {
    expect(state.href.includes(value), `${label}: URL`).toBe(false);
    expect(state.state.includes(value), `${label}: history.state`).toBe(false);
    expect(history.entries.some((entry) => entry.url.includes(value)), `${label}: a history entry URL`).toBe(false);
    expect(text.includes(value), `${label}: page text`).toBe(false);
    expect(allRequestUrls.some((url) => url.includes(value)), `${label}: a request URL`).toBe(false);
  }
  await cdp.detach();
}

/** axe at 1280 and 320 px, no horizontal scroll at 320, and a keyboard pass (every control reachable, focus visible). */
async function checkState(page: Page, label: string) {
  await page.setViewportSize({ width: 1280, height: 800 });
  const axe1280 = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  expect(axe1280.violations.map((violation) => `${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join('; ')}`), `${label}: axe 1280`).toEqual([]);
  const h1 = await page.getByRole('heading', { level: 1 }).count();
  expect(h1, `${label}: one h1`).toBe(1);

  const expectedStops = await page.locator('main a[href], main button, main input, main [tabindex="0"], header a, header button').evaluateAll(
    (elements) => elements.filter((element) => {
      const style = getComputedStyle(element);
      return style.visibility !== 'hidden' && style.display !== 'none';
    }).length);
  const visited: string[] = [];
  const invisibleFocus: string[] = [];
  await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
  for (let press = 0; press < expectedStops + 4; press++) {
    await page.keyboard.press('Tab');
    const stop = await page.evaluate(() => {
      const element = document.activeElement as HTMLElement | null;
      if (!element || element === document.body) return null;
      const style = getComputedStyle(element);
      const visible = (parseFloat(style.outlineWidth) > 0 && style.outlineStyle !== 'none') || style.boxShadow !== 'none';
      const name = element.getAttribute('aria-label') ?? (element.id ? document.querySelector(`label[for="${element.id}"]`)?.textContent : null) ?? element.textContent ?? element.tagName;
      return { name: `${element.tagName.toLowerCase()}:${(name ?? '').trim().slice(0, 40)}`, visible };
    });
    if (!stop) continue;
    if (visited.includes(stop.name)) break;
    visited.push(stop.name);
    if (!stop.visible) invisibleFocus.push(stop.name);
  }
  expect(invisibleFocus, `${label}: a focused control with no visible focus indicator`).toEqual([]);
  const controls = await page.locator('main a[href], main button, main input').count();
  expect(visited.length, `${label}: keyboard reaches every control (${visited.join(' > ')})`).toBeGreaterThanOrEqual(controls);

  await page.setViewportSize({ width: 320, height: 800 });
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal scroll at 320`).toBeLessThanOrEqual(0);
  const axe320 = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  expect(axe320.violations.map((violation) => violation.id), `${label}: axe 320`).toEqual([]);
  await page.setViewportSize({ width: 1280, height: 800 });
  note(`${label}: axe clean at 1280 and 320, no horizontal scroll at 320, keyboard order [${visited.join(' > ')}]`);
}

const focusedText = (page: Page) => page.evaluate(() => (document.activeElement as HTMLElement | null)?.id || document.activeElement?.textContent?.trim().slice(0, 40) || document.activeElement?.tagName);

// ---- setup ------------------------------------------------------------------------------------------------------------------------

test.beforeAll(async () => {
  test.setTimeout(300_000);
  logins.push(...(await createLogins()));
});

// ---- journey 1: details, 202, 2b, wrong codes, lock, then a rate limit on 2b ---------------------------------------------

test('login 1: 2a bounds, 202 to 2b, two wrong codes with tries left, the third locks, start again, 429 with Retry-After on 2b', async ({ browser }) => {
  test.setTimeout(240_000);
  await restartApi();
  const login = logins[0];
  const { context, page } = await signIn(browser, login);
  const calls = recordLinking(page);
  await page.goto('/');
  await expect(page).toHaveURL(`${origin}/link`);

  // 2a: the page.
  await expect(page.getByRole('heading', { level: 1, name: 'Link your login' })).toBeVisible();
  await expect(page).toHaveTitle('Link your login · DisputeDesk');
  for (const field of [idNumber(page), accountNumber(page)]) {
    await expect(field).toHaveAttribute('inputmode', 'numeric');
    await expect(field).toHaveAttribute('autocomplete', 'off');
    await expect(field).toHaveAttribute('spellcheck', 'false');
  }
  await expect(page.getByText("Can't link online? Contact the bank.")).toBeVisible();
  await checkState(page, '2a empty');

  // 2a: every bad value shows its message and the summary, and sends nothing.
  const sendInvalid = async (id: string, account: string) => {
    await idNumber(page).fill(id);
    await accountNumber(page).fill(account);
    await page.getByRole('button', { name: 'Continue' }).click();
  };
  await sendInvalid('', '');
  await expect(page.getByRole('group', { name: 'Check the details below' })).toBeFocused();
  await expect(page.getByText('Enter a valid 13-digit ID number.')).toHaveCount(2);
  await expect(page.getByText('Enter a valid account number of 6 to 16 digits.')).toHaveCount(2);
  await checkState(page, '2a field errors');
  for (const [id, account] of [['900101500008', '1000000031'], ['90010150000855', '1000000031'], ['9001015000086', '1000000031'], ['9001 15000085', '1000000031'], ['900101500008a', '1000000031'], [customerD.idNumber, '12345'], [customerD.idNumber, '12345678901234567']] as const) {
    await sendInvalid(id, account);
    await expect(page.getByRole('group', { name: 'Check the details below' })).toBeVisible();
    expect(await idNumber(page).inputValue(), 'the field keeps what was typed').toBe(id);
  }
  expect(calls, 'client validation sends no request').toEqual([]);
  // the summary link points at a field id and focuses it
  await page.getByRole('link', { name: 'Enter a valid account number of 6 to 16 digits.' }).click();
  await expect(accountNumber(page)).toBeFocused();
  await expect(page.getByRole('link', { name: 'Enter a valid account number of 6 to 16 digits.' })).toHaveAttribute('href', '#accountNumber');

  // 2a -> 202 -> 2b (customer D, a real OTP).
  const smsBefore = (await smsToD()).length;
  await submitDetails(page, customerD.idNumber, customerD.accountNumber);
  await expect(page).toHaveURL(`${origin}/link/otp`);
  await expect(page.getByRole('heading', { level: 1, name: 'Enter your OTP' })).toBeFocused();
  await expect(page).toHaveTitle('Enter your OTP · DisputeDesk');
  expect(calls).toHaveLength(1);
  expect(calls[0]).toMatchObject({ path: '/api/v1/linking/start', status: 202, xsrf: true, query: '' });
  expect(JSON.parse(calls[0].body!)).toEqual({ idNumber: customerD.idNumber, accountNumber: customerD.accountNumber });
  await expect(page.getByText("If these details match our records, we've sent a code to the cellphone number we hold for you.")).toBeVisible();
  await expect(page.getByText('The code expires 5 minutes after we send it.')).toBeVisible();
  expect(await page.locator('main').innerText()).not.toMatch(/\+?27\d|\*{2}|\d{3}\s?\*/); // no masked or full cellphone
  await nextCodeForD(smsBefore);
  await until('the challenge to hold an OTP', () => challengeOf(login.sub).startsWith('0|CUS-000004|true|true'));
  note(`login 1 DB: LinkingChallenges latest = ${challengeOf(login.sub)} (Attempts|Customer|hasOtp|sent|consumed|superseded)`);
  const otp = code(page);
  await expect(otp).toHaveAttribute('autocomplete', 'one-time-code');
  await expect(otp).toHaveAttribute('inputmode', 'numeric');
  await expect(otp).toHaveAttribute('spellcheck', 'false');
  expect(await otp.getAttribute('maxlength')).toBeNull();
  await checkState(page, '2b initial');

  // 2b: typing five digits, a letter: no request; six digits never submit by themselves.
  await otp.fill('12345');
  await page.waitForTimeout(500);
  expect(calls, 'typing sends nothing').toHaveLength(1);
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByText('Enter the 6-digit code.')).toHaveCount(2);
  await expect(page.getByRole('group', { name: 'Check the details below' })).toBeFocused();
  await checkState(page, '2b field error');
  await otp.fill('12345a');
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByText('Enter the 6-digit code.')).toHaveCount(2);
  await otp.fill(wrongCodes[0].slice(0, 3) + ' ' + wrongCodes[0].slice(3)); // pasted with a space
  await page.waitForTimeout(500);
  expect(calls, 'six digits typed or pasted do not submit').toHaveLength(1);

  // wrong code 1: "111 111" is sent as "111111".
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByRole('alert').filter({ hasText: "That code didn't work. You have 2 tries left." })).toBeVisible();
  expect(calls[1]).toMatchObject({ path: '/api/v1/linking/verify', status: 422, xsrf: true });
  expect(JSON.parse(calls[1].body!)).toEqual({ code: '111111' });
  expect(typeof JSON.parse(calls[1].body!).code).toBe('string');
  expect(await otp.inputValue(), 'the input is emptied').toBe('');
  await expect(otp).toBeFocused();
  await expect(otp).toHaveAttribute('aria-invalid', 'true');
  note(`login 1 network: verify #1 -> 422 otp-incorrect; DB challenge = ${challengeOf(login.sub)}`);
  await checkState(page, '2b wrong code (2 tries left)');
  // wrong code 2
  await otp.fill(wrongCodes[1]);
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByRole('alert').filter({ hasText: "That code didn't work. You have 1 try left." })).toBeVisible();
  expect(calls[2]).toMatchObject({ status: 422 });
  await expect(otp).toBeFocused();
  await expectNothingKept(page, context, [customerD.idNumber, customerD.accountNumber, ...wrongCodes], 'login 1 on 2b');
  await checkState(page, '2b wrong code (1 try left)');
  // wrong code 3 locks
  await otp.fill(wrongCodes[2]);
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByRole('heading', { level: 1, name: "We can't link you online" })).toBeFocused();
  expect(calls[3]).toMatchObject({ path: '/api/v1/linking/verify', status: 409 });
  expect(calls[3].contentType).toMatch(/^application\/problem\+json/);
  await expect(page).toHaveTitle("We can't link you online · DisputeDesk");
  await expect(page.getByText('Please contact the bank.', { exact: true })).toBeVisible();
  await expect(page.getByText('If you think you mistyped your details or the code, you can start again.')).toBeVisible();
  expect(await page.locator('main').innerText()).not.toMatch(/\b[0-3]\b.*tr(y|ies)/);
  await expect(page.getByText("Can't link online? Contact the bank.")).toBeVisible();
  note(`login 1 DB after lock: challenge = ${challengeOf(login.sub)}, audit = ${auditRows(login.sub).join(' ; ')}`);
  await checkState(page, '2b locked');

  // Start again -> 2a, no request; a no-match start (permit 5 of 5) goes to 2b with the same neutral text.
  const before = calls.length;
  await page.getByRole('link', { name: 'Start again' }).click();
  await expect(page).toHaveURL(`${origin}/link`);
  expect(calls).toHaveLength(before);
  await submitDetails(page, customerD.idNumber, noMatchAccount);
  await expect(page).toHaveURL(`${origin}/link/otp`);
  expect(calls[4]).toMatchObject({ path: '/api/v1/linking/start', status: 202 });
  const neutralNoMatch = (await page.locator('main').innerText()).replace(/\s+/g, ' ');

  // permit 6: verify -> 429 with Retry-After 900 (the 6.3 per-login limit).
  await otp.fill('123456');
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByRole('alert').filter({ hasText: 'Too many attempts. Try again in 15 minutes.' })).toBeVisible();
  expect(calls[5]).toMatchObject({ path: '/api/v1/linking/verify', status: 429, retryAfter: '900' });
  expect(calls[5].contentType).toMatch(/^application\/problem\+json/);
  await expect(page.getByRole('button', { name: 'Verify' })).toHaveAttribute('aria-disabled', 'true');
  expect(await otp.inputValue(), 'the field keeps its value').toBe('123456');
  await otp.fill('654321'); // still editable
  await page.getByRole('button', { name: 'Verify' }).click({ force: true });
  await page.waitForTimeout(700);
  expect(calls, 'the waiting button sends nothing').toHaveLength(6);
  expect(await page.locator('body').innerText()).not.toMatch(/\d+ seconds|\bcount/i);
  await checkState(page, '2b rate limited with Retry-After 900');
  note(`login 1 DB audit: ${auditRows(login.sub).join(' ; ')}`);
  expect(auditRows(login.sub).filter((row) => row.startsWith('LinkingRateLimited|'))).toHaveLength(1);

  await expectNothingKept(page, context, [customerD.idNumber, customerD.accountNumber, noMatchAccount, ...wrongCodes, '123456', '654321'], 'login 1 end');
  (globalThis as { neutral?: string }).neutral = neutralNoMatch;
  await context.close();
});

// ---- journey 2: 429 with Retry-After on 2a -----------------------------------------------------------------------------------

test('login 2: five starts spend the login limit, the sixth shows the 15-minute notice on 2a with the button waiting', async ({ browser }) => {
  test.setTimeout(240_000);
  await restartApi();
  const { context, page } = await signIn(browser, logins[1]);
  const calls = recordLinking(page);
  await page.goto('/link');
  for (let attempt = 1; attempt <= 5; attempt++) {
    await submitDetails(page, customerD.idNumber, noMatchAccount);
    await expect(page).toHaveURL(`${origin}/link/otp`);
    if (attempt === 1) {
      // The same neutral text for details that match nothing as for a real match (2b opened directly shows it too).
      await expect(page.getByText("If these details match our records, we've sent a code to the cellphone number we hold for you.")).toBeVisible();
      const noMatchText = (await page.locator('main').innerText()).replace(/\s+/g, ' ');
      expect(noMatchText).toBe((globalThis as { neutral?: string }).neutral);
    }
    await page.getByRole('link', { name: 'Start again' }).click();
    await expect(page).toHaveURL(`${origin}/link`);
  }
  expect(calls.map((call) => call.status)).toEqual([202, 202, 202, 202, 202]);
  await submitDetails(page, customerD.idNumber, noMatchAccount);
  await expect(page.getByRole('alert').filter({ hasText: 'Too many attempts. Try again in 15 minutes.' })).toBeVisible();
  expect(calls[5]).toMatchObject({ path: '/api/v1/linking/start', status: 429, retryAfter: '900' });
  await expect(page).toHaveURL(`${origin}/link`);
  await expect(page.getByRole('button', { name: 'Continue' })).toHaveAttribute('aria-disabled', 'true');
  await accountNumber(page).fill('1000000050'); // fields stay editable
  await page.getByRole('button', { name: 'Continue' }).click({ force: true });
  await page.waitForTimeout(700);
  expect(calls, 'the waiting button sends nothing').toHaveLength(6);
  await checkState(page, '2a rate limited with Retry-After 900');
  // 2a rate limited by the same login does not leave /link and does not echo values
  await expectNothingKept(page, context, [customerD.idNumber, customerD.accountNumber, noMatchAccount, '1000000050'], 'login 2 end');
  await context.close();
});

// ---- journey 3: a real link ------------------------------------------------------------------------------------------------------

test('login 3: 2a -> 202 -> 2b, two wrong codes, the real code from the SMS sink links, lands on /transactions and stays', async ({ browser }) => {
  test.setTimeout(240_000);
  await restartApi();
  const login = logins[2];
  const { context, page } = await signIn(browser, login);
  const calls = recordLinking(page);
  const transactionCalls: number[] = [];
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/api/v1/transactions') transactionCalls.push(response.status());
  });
  await page.goto('/link');
  const smsBefore = (await smsToD()).length;
  const topicBefore = topicRecords().length;
  await submitDetails(page, customerD.idNumber, customerD.accountNumber);
  await expect(page).toHaveURL(`${origin}/link/otp`);
  const otpCode = await nextCodeForD(smsBefore);
  expect(wrongCodes).not.toContain(otpCode);

  // Reload and a direct open of /link/otp show the same 2b, neutral text included (the challenge is on the server).
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'Enter your OTP' })).toBeVisible();
  await expect(page.getByText("If these details match our records, we've sent a code to the cellphone number we hold for you.")).toBeVisible();

  await code(page).fill(wrongCodes[0]);
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByText("That code didn't work. You have 2 tries left.")).toBeVisible();
  await code(page).fill(wrongCodes[1]);
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByText("That code didn't work. You have 1 try left.")).toBeVisible();
  note(`login 3 DB before the correct code: challenge = ${challengeOf(login.sub)}`);

  // The correct third code links. Watch the live region from the click on.
  await page.evaluate(() => {
    (window as unknown as { announced: string[] }).announced = [];
    new MutationObserver(() => {
      for (const region of document.querySelectorAll('[aria-live]')) {
        if (region.textContent) (window as unknown as { announced: string[] }).announced.push(region.textContent);
      }
    }).observe(document.body, { subtree: true, childList: true, characterData: true });
  });
  await code(page).fill(otpCode.slice(0, 3) + ' ' + otpCode.slice(3));
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page).toHaveURL(`${origin}/transactions`);
  const verifyCalls = calls.filter((call) => call.path === '/api/v1/linking/verify');
  expect(verifyCalls.map((call) => call.status)).toEqual([422, 422, 204]);
  expect(JSON.parse(verifyCalls[2].body!)).toEqual({ code: otpCode });
  await expect(page.getByRole('heading', { level: 1 })).not.toHaveText('Enter your OTP');
  expect(await page.evaluate(() => (window as unknown as { announced: string[] }).announced)).toContain('Your login is linked.');
  await page.waitForTimeout(3000);
  await expect(page).toHaveURL(`${origin}/transactions`); // stays: no bounce back to /link
  await until('the transactions request', () => transactionCalls.length > 0, 10);
  expect(transactionCalls.every((status) => status === 200)).toBe(true);
  // history.replace: Back does not return to 2b
  await page.goBack().catch(() => undefined);
  await page.waitForTimeout(500);
  expect(new URL(page.url()).pathname).not.toBe('/link/otp');
  await page.goto('/link');
  await expect(page).toHaveURL(`${origin}/transactions`); // SessionGate sends a linked customer away
  expect(calls.filter((call) => call.path === '/api/v1/linking/start')).toHaveLength(1);
  await page.reload();
  await expect(page).toHaveURL(`${origin}/transactions`);

  // Database and events.
  expect(linkRow(login.sub)).toBe(customerD.customerId);
  expect(challengeOf(login.sub)).toMatch(/^3\|CUS-000004\|true\|true\|true\|false$/);
  const audit = auditRows(login.sub);
  note(`login 3 DB: CustomerLinks -> ${linkRow(login.sub)}; challenge = ${challengeOf(login.sub)}; audit = ${audit.join(' ; ')}`);
  expect(audit.some((row) => row.startsWith('LinkingOtpSent|'))).toBe(true);
  expect(audit.some((row) => row.startsWith('LinkingLinked|') || row.startsWith('LinkingVerified|') || /Link/.test(row))).toBe(true);
  expect(topicRecords().length, 'linking emits no event on the topic').toBe(topicBefore);
  await expectNothingKept(page, context, [customerD.idNumber, customerD.accountNumber, otpCode, ...wrongCodes], 'login 3 end');
  await context.close();
});

// ---- journey 4: a start for an already-linked customer looks the same ----------------------------------------------------

test('login 4: details of an already-linked customer give the same 202 and the same 2b', async ({ browser }) => {
  test.setTimeout(120_000);
  await restartApi();
  const { context, page } = await signIn(browser, logins[3]);
  const calls = recordLinking(page);
  await page.goto('/link');
  await submitDetails(page, customerD.idNumber, customerD.accountNumber);
  await expect(page).toHaveURL(`${origin}/link/otp`);
  expect(calls[0].status).toBe(202);
  expect((await page.locator('main').innerText()).replace(/\s+/g, ' ')).toBe((globalThis as { neutral?: string }).neutral);
  await code(page).fill('123456');
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page.getByText("That code didn't work. You have 2 tries left.")).toBeVisible(); // the same copy as no challenge
  note(`login 4 (already-linked details): start 202, 2b text identical to the no-match case, verify -> 422 with 2 tries left; challenge = ${challengeOf(logins[3].sub)}`);
  await context.close();
});

// ---- journey 5: the full queue, a 429 with no Retry-After -----------------------------------------------------------------

function holdCustomerLock(): { release: () => Promise<void> } {
  const secondKey = `(('x' || substr(encode(sha256(convert_to('${customerD.customerId}', 'UTF8')), 'hex'), 1, 8))::bit(32)::int)`;
  const child: ChildProcessWithoutNullStreams = spawn('docker',
    ['exec', '-i', '-e', `PGPASSWORD=${process.env.QA_DB_PASSWORD ?? 'dev-only-qa-db'}`, 'disputedesk-postgres-1', 'psql', '-h', 'localhost', '-U', 'qa_readonly', '-d', 'disputedesk', '-At'],
    { stdio: 'pipe' });
  child.stdin.write(`select pg_advisory_lock(6005, ${secondKey});\n`);
  return { release: () => new Promise((resolve) => { child.on('close', () => resolve()); child.stdin.end(); }) };
}
const lockWaiters = () => Number(sql(`select count(*) from pg_locks where locktype = 'advisory' and not granted`));

test('full queue: the sixth start gets 429 with no Retry-After, "Try again later.", and the button stays usable', async ({ browser }) => {
  test.skip(queueCapacity === 0, 'needs the Api started with Linking:QueueCapacity=1 (E2E_LINKING_QUEUE_CAPACITY=1)');
  test.setTimeout(300_000);
  await restartApi();
  const holder = holdCustomerLock();
  try {
    await until('the lock holder', () => Number(sql(`select count(*) from pg_locks where locktype = 'advisory' and granted and classid = 6005`)) === 1);
    const accepted = 4 + queueCapacity;
    for (let index = 0; index < accepted; index++) {
      const { context, page } = await signIn(browser, logins[4 + index]);
      const token = (await (await context.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
      const response = await context.request.post('/api/v1/linking/start', {
        data: JSON.stringify({ idNumber: customerD.idNumber, accountNumber: customerD.accountNumber }),
        headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }, maxRedirects: 0,
      });
      expect(response.status(), `accepted start ${index + 1}`).toBe(202);
      await page.close();
      await context.close();
      if (index === 3) await until('4 jobs waiting on the lock', () => lockWaiters() === 4);
    }
    const { context, page } = await signIn(browser, logins[4 + accepted]);
    const calls = recordLinking(page);
    await page.goto('/link');
    await submitDetails(page, customerD.idNumber, customerD.accountNumber);
    await expect(page.getByRole('alert').filter({ hasText: 'Too many attempts. Try again later.' })).toBeVisible();
    expect(calls[0]).toMatchObject({ path: '/api/v1/linking/start', status: 429, retryAfter: null });
    expect(await page.locator('main').innerText()).not.toMatch(/minute/);
    await expect(page.getByRole('button', { name: 'Continue' })).not.toHaveAttribute('aria-disabled', 'true');
    await expect(page).toHaveURL(`${origin}/link`);
    await checkState(page, '2a rate limited without Retry-After (full queue)');
    // the button works again at once: a second press sends a request
    await page.getByRole('button', { name: 'Continue' }).click();
    await until('a second start request', () => calls.length === 2, 10);
    await expectNothingKept(page, context, [customerD.idNumber, customerD.accountNumber], 'full queue end');
    await context.close();
  } finally {
    await holder.release();
  }
});

// ---- the typed values in telemetry, logs and the topic -----------------------------------------------------------------------

test('no typed value in any network URL, the Aspire spans or logs, the Api logs or the topic', async () => {
  const secrets = [customerD.idNumber, customerD.accountNumber, noMatchAccount, ...everyCodeSeen];
  for (const url of allRequestUrls) for (const secret of secrets) expect(url.includes(secret), `a request URL holds a typed value`).toBe(false);
  expect(allRequestUrls.filter((url) => url.includes('/api/v1/linking/') && url.includes('?'))).toEqual([]);
  const spans = await (await fetch(`${dashboard}/api/telemetry/spans?limit=5000`)).text();
  const logs = await (await fetch(`${dashboard}/api/telemetry/logs?limit=5000`)).text();
  const apiLogs = execFileSync('sh', ['-c', `docker logs --since ${startedAt} disputedesk-api-1 2>&1`], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  expect(spans.length).toBeGreaterThan(1000);
  note(`spans scanned: ${spans.length} chars, containing /api/v1/linking/start ${spans.includes('/api/v1/linking/start')} and verify ${spans.includes('/api/v1/linking/verify')}`);
  for (const [name, text] of [['spans', spans], ['dashboard logs', logs], ['api logs', apiLogs], ['topic', JSON.stringify(topicRecords())]] as const) {
    for (const secret of secrets) expect(text.includes(secret), `${name} holds a typed value`).toBe(false);
  }
});
