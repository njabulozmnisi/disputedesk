import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { expect, request as playwrightRequest, test, type Page } from '@playwright/test';
import { expectProblemShape, messageText, origin, pause, queryDatabase, registerLogin, sessionCookieName, smsTo, topicRecords } from '../support';
import { axeTags, expectAccessibleAtBothWidths } from './screens';

// Story 7.1, UJ-1: a new login signs up, links to customer D, raises an unrecognised dispute on TXN-D-0030 and signs out,
// then the one dispute is traced through the browser, the network, the database (qa_readonly) and the topic. Customer D
// links once per database, so this project runs on its own fresh stack (docker compose down -v, then up --wait).
// The ID number, account number, OTP and cookies are used but never printed.

const startedAt = new Date().toISOString();
const fixture = JSON.parse(readFileSync(fileURLToPath(new URL('../../../fixtures/local.json', import.meta.url)), 'utf8'));
const customerD = (fixture.customers as { key: string; customerId: string; idNumber: string; cellphone: string; accounts: { accountNumber: string }[] }[]).find((entry) => entry.key === 'D');
if (!customerD) throw new Error('fixtures/local.json has no customer D');
const disputesPath = '/api/v1/disputes';
const note = "I don't recognise this merchant.";

const rows = <T>(sql: string): T[] => JSON.parse(queryDatabase(`select coalesce(json_agg(t), '[]') from (${sql}) t`)) as T[];
const evidence = (line: string) => console.log(`EVIDENCE ${line}`);

/** The hosted (Zitadel) pages are scanned and recorded, never failed on: DisputeDesk only themes them (AD-6, story 2.6). */
async function recordHostedAxe(page: Page, stage: 'register' | 'password' | 'verify') {
  const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  const found = result.violations.map((violation) => `${violation.id} (${violation.impact}, ${violation.nodes.length} nodes)`);
  evidence(`hosted ${stage} page axe: ${found.length === 0 ? 'no violations' : found.join('; ')}`);
}

async function otpFor(cellphone: string): Promise<string> {
  const deadline = Date.now() + 40_000;
  while (Date.now() < deadline) {
    const messages = await smsTo(cellphone, startedAt);
    // Mailpit lists newest first.
    const code = messages.length > 0 ? (await messageText(messages[0].ID)).match(/\b(\d{6})\b/)?.[1] : undefined;
    if (code) return code;
    await pause(500);
  }
  throw new Error('no OTP SMS reached customer D');
}

test('UJ-1: sign up, link to D, raise on TXN-D-0030, sign out, traced to the database and the topic', async ({ browser }) => {
  test.setTimeout(300_000);

  // ---- sign up and verify the e-mail (hosted pages) ------------------------------------------------------------------
  const login = await registerLogin(browser, 'uj1', 'run', recordHostedAxe);
  const page = await login.context.newPage();

  // ---- link: 2a, the error summary, 2b ----------------------------------------------------------------------------------
  await page.goto('/link');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'link details');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('group', { name: 'Check the details below' })).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'link details with error summary');
  await page.getByLabel('ID number').fill(customerD.idNumber);
  await page.getByLabel('Account number').fill(customerD.accounts[0].accountNumber);
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page).toHaveURL(`${origin}/link/otp`);
  await expect(page.getByText("If these details match our records, we've sent a code to the cellphone number we hold for you.")).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'link OTP');
  await page.getByLabel('6-digit one-time code').fill(await otpFor(customerD.cellphone));
  await page.getByRole('button', { name: 'Verify' }).click();
  await expect(page).toHaveURL(`${origin}/transactions`);

  // ---- transactions: the pending row is not disputable, TXN-D-0030 is -----------------------------------------------------
  const settledRow = page.getByRole('listitem').filter({ hasText: 'ZX Digital Services' }).filter({ hasText: '849.00' });
  const pendingRow = page.getByRole('listitem').filter({ hasText: 'Checkers Sixty60' }).filter({ hasText: '319.73' });
  await expect(settledRow.getByRole('link', { name: 'Dispute this transaction' })).toBeVisible();
  await expect(pendingRow).toContainText('Pending. You can dispute it once it has settled.');
  await expect(pendingRow.getByRole('link', { name: /Dispute/ })).toHaveCount(0);
  await expectAccessibleAtBothWidths(page, 'transactions');

  // ---- raise: reason, note, review, submit ----------------------------------------------------------------------------------
  await settledRow.getByRole('link', { name: 'Dispute this transaction' }).click();
  await expect(page.getByRole('heading', { name: 'Dispute this transaction' })).toBeVisible();
  await page.getByRole('radio', { name: 'Unrecognised', exact: true }).click();
  await page.getByLabel('Note (optional)').fill(note);
  await expectAccessibleAtBothWidths(page, 'raise reason');
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { name: 'Review your dispute' })).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'raise review');

  const created = page.waitForResponse((response) => new URL(response.url()).pathname === disputesPath && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Submit dispute' }).click();
  const response = await created;

  // ---- network (criterion 3) -------------------------------------------------------------------------------------------------
  expect(response.status()).toBe(201);
  const requestHeaders = response.request().headers();
  expect(requestHeaders['idempotency-key'], 'Idempotency-Key header sent').toBeTruthy();
  expect(requestHeaders['x-xsrf-token'], 'X-XSRF-TOKEN header sent').toBeTruthy();
  const disputeId = (await response.json()).disputeId as string;
  expect(disputeId).toMatch(/^[0-9a-f-]{36}$/);

  await expect(page).toHaveURL(`${origin}/disputes/${disputeId}`);
  await expect(page.getByText('Dispute submitted', { exact: true }).first()).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'raise submitted');

  // ---- browser: the row badge, the history, the detail timeline ----------------------------------------------------------------
  await page.goto('/transactions');
  await expect(settledRow.getByRole('link', { name: /Dispute status: Submitted/ })).toBeVisible();
  await expect(settledRow.getByRole('link', { name: 'Dispute this transaction' })).toHaveCount(0);
  await page.goto('/disputes');
  await expect(page.getByRole('heading', { level: 1, name: 'Disputes' })).toBeVisible();
  await expect(page.getByRole('link', { name: /ZX Digital Services/ })).toHaveCount(1);
  await expectAccessibleAtBothWidths(page, 'dispute history');
  await page.getByRole('link', { name: /ZX Digital Services/ }).click();
  await expect(page).toHaveURL(`${origin}/disputes/${disputeId}`);
  const timeline = page.getByRole('region', { name: 'Timeline' });
  await expect(timeline.getByRole('listitem')).toHaveCount(1);
  await expect(timeline.getByRole('listitem')).toContainText('Submitted');
  await expect(timeline.locator('time')).toHaveText(/ SAST$/);
  await expect(timeline.getByRole('listitem')).toContainText('You ·');
  await expectAccessibleAtBothWidths(page, 'dispute detail');

  // ---- database (criterion 3), scoped to the dispute ID ----------------------------------------------------------------------------
  const disputeRows = rows<{ TransactionId: string; Status: string; Reason: string; Version: number }>(`select "TransactionId", "Status", "Reason", "Version" from "Disputes" where "Id" = '${disputeId}'`);
  expect(disputeRows).toEqual([{ TransactionId: 'TXN-D-0030', Status: 'Submitted', Reason: 'Unrecognised', Version: 1 }]);
  const storedEvents = rows<{ Id: string; EventType: string; Version: number }>(`select "Id", "EventType", "Version" from "StoredEvents" where "DisputeId" = '${disputeId}'`);
  expect(storedEvents).toHaveLength(1);
  expect(storedEvents[0]).toMatchObject({ EventType: 'DisputeRaised', Version: 1 });
  const outbox = rows<{ Id: string }>(`select "Id" from "OutboxMessages" where "DisputeId" = '${disputeId}'`);
  expect(outbox).toHaveLength(1);
  expect(outbox[0].Id).toBe(storedEvents[0].Id);
  await expect
    .poll(() => rows<{ Status: string }>(`select "Status" from "OutboxMessages" where "DisputeId" = '${disputeId}'`)[0]?.Status, { timeout: 30_000, intervals: [1000] })
    .toBe('Sent');
  expect(rows(`select 1 from "AuditLogEntries" where "DisputeId" = '${disputeId}'`)).toHaveLength(0);

  // ---- topic (criterion 4) -----------------------------------------------------------------------------------------------------------
  const records = topicRecords().filter((record) => record.key === disputeId);
  expect(records.length).toBeGreaterThanOrEqual(1);
  for (const record of records) {
    const event = JSON.parse(record.value);
    expect(event).toMatchObject({ specversion: '1.0', type: 'disputedesk.dispute.raised.v1', id: storedEvents[0].Id });
    expect(event.source, 'CloudEvent source').toBeTruthy();
    expect(record.headers.find((header) => header.key === 'content-type')?.value).toBe('application/cloudevents+json');
    expect(record.headers.map((header) => header.key)).toContain('traceparent');
  }

  // ---- sign-out and session end (criterion 5) ---------------------------------------------------------------------------------------------
  const linkedSub = rows<{ LoginSub: string }>(`select "LoginSub" from "CustomerLinks" where "CustomerId" = '${customerD.customerId}'`);
  expect(linkedSub).toEqual([{ LoginSub: login.sub }]);
  const sessionCookie = (await login.context.cookies(origin)).find((cookie) => cookie.name === sessionCookieName)?.value;
  expect(sessionCookie, 'session cookie before sign-out').toBeTruthy();

  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.waitForURL(`${origin}/`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'start page after sign-out');
  await expect.poll(() => rows<{ Action: string }>(`select "Action" from "AuditLogEntries" where "ActorId" = '${login.sub}'`).map((row) => row.Action).sort(), { timeout: 30_000 }).toEqual(expect.arrayContaining(['SignIn', 'SignOut']));
  const audit = rows<{ Action: string; Later: boolean }>(`select "Action", "OccurredAtUtc" > '${startedAt}' as "Later" from "AuditLogEntries" where "ActorId" = '${login.sub}' and "Action" in ('SignIn', 'SignOut')`);
  expect(audit.every((row) => row.Later), 'sign-in and sign-out are later than the run start').toBe(true);

  const replayContext = await playwrightRequest.newContext({ baseURL: origin });
  const replay = await replayContext.get('/api/v1/transactions', { headers: { Cookie: `${sessionCookieName}=${sessionCookie}` } });
  expect(replay.status()).toBe(401);
  expect(expectProblemShape(await replay.json(), 'unauthorized', 401)).toEqual([]);
  await replayContext.dispose();

  await page.goto('/signed-out?reason=expired');
  await expect(page.getByRole('heading', { level: 1, name: "You've been signed out" })).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'signed out');

  // The identity provider's session ended too: the hosted login shows the account picker or the username page, then asks for the password.
  const silentSignIn: string[] = [];
  page.on('request', (request) => request.isNavigationRequest() && request.url().includes('/api/signin-oidc') && silentSignIn.push(request.url()));
  await page.goto('/api/v1/bff/login');
  const account = page.getByRole('button', { name: new RegExp(login.email) });
  const usernameField = page.getByTestId('username-text-input');
  const passwordField = page.getByTestId('password-text-input');
  await expect(account.or(usernameField).or(passwordField)).toBeVisible();
  if (await account.isVisible()) await account.click();
  if (await usernameField.isVisible()) {
    await usernameField.fill(login.email);
    await usernameField.press('Enter');
  }
  await expect(passwordField).toBeVisible();
  expect(new URL(page.url()).pathname).toBe('/ui/v2/login/password');
  expect(silentSignIn).toEqual([]);
  await login.context.close();
});
