import { expect, test } from '@playwright/test';
import { enterHostedCredentials, fixtureLogin, origin, queryDatabase, queryRows, signInContext } from './support';

// Story 7.13 (backend hardening), walked on the real stack: the OIDC transient cookies' names and attributes with the
// local configured names (criterion 1), a refused raise audited (criterion 4) and sign-out audited (criterion 5). Database through qa_readonly.
// The raise rejection counter (criterion 8) is read on the Aspire dashboard's metrics page, which has no API, so the QA record walks it.
// Nothing here prints a cookie value, token or personal data: assertions compare, they do not echo.
test.describe.configure({ mode: 'serial' });

type ParsedCookie = { name: string; attributes: Map<string, string>; deleted: boolean };

/** One `Set-Cookie` line as name plus lower-cased attributes (no value kept). */
function parseSetCookie(line: string): ParsedCookie {
  const [pair, ...parts] = line.split(';').map((part) => part.trim());
  const attributes = new Map<string, string>();
  for (const part of parts) {
    const [key, ...rest] = part.split('=');
    attributes.set(key.toLowerCase(), rest.join('='));
  }
  const expires = attributes.get('expires');
  const deleted = (expires !== undefined && new Date(expires).getTime() < Date.now()) || attributes.get('max-age') === '0';
  return { name: pair.split('=')[0], attributes, deleted };
}

const isTransient = (cookie: ParsedCookie) => /^disputedesk-(correlation|nonce)\./.test(cookie.name);

test('criterion 1: the OIDC correlation and nonce cookies carry the configured names, Path=/, Secure, SameSite=None, HttpOnly, and are deleted on the callback; sign-in completes on http://localhost:8080', async ({ browser }) => {
  const login = fixtureLogin('customer-a');
  const context = await browser.newContext();
  const page = await context.newPage();
  const setOnLogin: ParsedCookie[] = [];
  const setOnCallback: ParsedCookie[] = [];
  page.on('response', async (response) => {
    const path = new URL(response.url()).pathname;
    if (path !== '/api/v1/authentication/login' && path !== '/api/signin-oidc') return;
    const lines = (await response.headersArray()).filter((header) => header.name.toLowerCase() === 'set-cookie').map((header) => header.value);
    (path === '/api/v1/authentication/login' ? setOnLogin : setOnCallback).push(...lines.map(parseSetCookie));
  });

  await page.goto('/api/v1/authentication/login');
  await enterHostedCredentials(page, login);
  await page.waitForURL((url) => url.origin === origin, { timeout: 60_000 });
  await expect.poll(async () => (await (await page.request.get('/api/v1/authentication/session')).json()).state).toBe('linked');
  expect(page.url().startsWith('http://localhost:8080/')).toBe(true);

  const transientOnLogin = setOnLogin.filter(isTransient);
  expect(transientOnLogin.map((cookie) => cookie.name.split('.')[0]).sort()).toEqual(['disputedesk-correlation', 'disputedesk-nonce']);
  for (const cookie of transientOnLogin) {
    expect(cookie.deleted).toBe(false);
    expect(cookie.attributes.get('path')).toBe('/');
    expect(cookie.attributes.has('secure')).toBe(true);
    expect(cookie.attributes.get('samesite')?.toLowerCase()).toBe('none');
    expect(cookie.attributes.has('httponly')).toBe(true);
    expect(cookie.attributes.has('domain')).toBe(false);
  }

  const transientOnCallback = setOnCallback.filter(isTransient);
  expect(transientOnCallback.filter((cookie) => cookie.deleted).map((cookie) => cookie.name.split('.')[0]).sort()).toEqual(['disputedesk-correlation', 'disputedesk-nonce']);
  for (const cookie of transientOnCallback) {
    expect(cookie.attributes.get('path')).toBe('/');
    expect(cookie.attributes.has('secure')).toBe(true);
    expect(cookie.attributes.get('samesite')?.toLowerCase()).toBe('none');
    expect(cookie.attributes.has('domain')).toBe(false);
  }
  expect((await context.cookies(origin)).filter((cookie) => /^disputedesk-(correlation|nonce)\./.test(cookie.name))).toEqual([]);
  await context.close();
});

const auditCount = () => Number(queryDatabase('select count(*) from "AuditLogEntries"'));
type AuditRow = { Action: string; Result: string; ActorType: string };
const auditRowsAfter = (skip: number) => queryRows<AuditRow>(`select "Action", "Result", "ActorType" from "AuditLogEntries" order by "Id" offset ${skip}`);

const idempotencyKey = () => `"qa713-${Date.now()}-${Math.random().toString(36).slice(2, 8)}"`;
// A transaction that is not a settled card debit is refused 422 not-eligible and audited (found in A's list, so no ID is hard-coded). The refusal is not tied to a dispute: refusing a
// seeded dispute's transaction (409 already disputed) would add an audit row seeded-disputes.spec.ts counts, and every card debit of A and B is reserved.

test('criterion 4: a refused raise (422 not eligible) writes one Raise/Rejected audit row with no dispute, and no dispute row', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const token = (await (await context.request.get('/api/v1/antiforgery')).json()).requestToken;
  const rows = (await (await context.request.get('/api/v1/transactions?limit=50')).json()).items as { accountRef: string; transactionId: string; notDisputableReason: string | null }[];
  const notEligible = rows.find((row) => row.notDisputableReason === 'notEligible');
  expect(notEligible, 'a not-eligible row of A exists').toBeTruthy();
  const creditRaise = { accountRef: notEligible!.accountRef, transactionId: notEligible!.transactionId, reason: 'unrecognised', note: null, expectedAmount: null };
  const auditBefore = auditCount();
  const disputesBefore = Number(queryDatabase('select count(*) from "Disputes"'));
  const response = await context.request.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': idempotencyKey() }, data: creditRaise, maxRedirects: 0,
  });
  expect(response.status()).toBe(422);
  expect(auditRowsAfter(auditBefore)).toEqual([{ Action: 'Raise', Result: 'Rejected', ActorType: 'Customer' }]);
  expect(queryDatabase(`select "DisputeId" is null from "AuditLogEntries" order by "Id" desc limit 1`)).toBe('t');
  expect(Number(queryDatabase('select count(*) from "Disputes"'))).toBe(disputesBefore);
  await context.close();
});

test('criterion 5: sign-out writes one SignOut/Succeeded audit row and deletes the session row', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const page = await context.newPage();
  const { requestToken } = await (await page.request.get('/api/v1/antiforgery')).json();
  const auditBefore = auditCount();
  const sessionsBefore = Number(queryDatabase('select count(*) from "Sessions"'));
  await page.goto('/');
  await page.evaluate((token) => {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = '/api/v1/authentication/logout';
    form.innerHTML = `<input type="hidden" name="__RequestVerificationToken" value="${token}">`;
    document.body.appendChild(form);
    form.submit();
  }, requestToken);
  await page.waitForURL((url) => url.origin === origin && url.pathname === '/', { timeout: 60_000 });
  await expect.poll(async () => (await (await page.request.get('/api/v1/authentication/session')).json()).state).toBe('anonymous');
  expect(auditRowsAfter(auditBefore)).toEqual([{ Action: 'SignOut', Result: 'Succeeded', ActorType: 'Customer' }]);
  expect(Number(queryDatabase('select count(*) from "Sessions"'))).toBe(sessionsBefore - 1);
  await context.close();
});
