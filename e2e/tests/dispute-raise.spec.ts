import AxeBuilder from '@axe-core/playwright';
import { expect, request as playwrightRequest, test, type Browser, type BrowserContext, type Page, type Route } from '@playwright/test';
import { fixtureLogin, gatewayContentSecurityPolicy, problemTypeBase, queryDatabase } from './support';

// Story 3.6: the raise-a-dispute screens (4a, 4b) with an idempotent submit, through the real gateway, BFF, API,
// database and Zitadel, signed in as seeded customer A (B owns the 404 case). The database is read through qa_readonly.
// Refusals come from the real API by forging the one POST in flight (another transaction, another customer's row, a
// key already used); only the cases the API cannot be made to produce (an in-progress key, a 503, a 500, a 400) are
// stubbed, and each test says so. There is no outbox relay before story 4.1, so the events hop is the Pending outbox row.
// The data is not reset between runs: a run raises on whichever transactions are still disputable (about 3 per run).

const origin = process.env.APP_ORIGIN ?? 'http://localhost:8080';
const disputesPath = '/api/v1/disputes';
const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const quotedUuid = /^"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"$/;
/** The one console error accepted by decision (story 2.5 criterion 7): Radix's scroll-lock <style> under style-src 'self'. */
const knownRadixStyleViolation = /^Applying inline style violates the following Content Security Policy directive 'style-src 'self''\./;

type ApiRow = {
  transactionId: string;
  accountRef: string;
  amount: { amount: string; currency: string };
  disputable: boolean;
  notDisputableReason: string | null;
  description: string;
};
type Attempt = { key: string | undefined; hasAntiforgery: boolean; body: Record<string, unknown>; at: number };
type Step =
  | 'real'
  | 'lost-response' // the request reaches the API and is stored, the browser never gets the answer
  | { stub: number; slug?: string; extra?: Record<string, unknown>; errors?: Record<string, string[]> }
  | { forge: (body: Record<string, unknown>) => Record<string, unknown>; key?: string };

let customerA: BrowserContext;
let customerB: BrowserContext;
let oldKey = ''; // the key of the first real raise, reused for the 422 idempotency-mismatch case
let disputedRow: ApiRow; // a row of A that already has a dispute (made by the first test)

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

async function listRows(context: BrowserContext): Promise<ApiRow[]> {
  const response = await context.request.get('/api/v1/transactions?limit=50');
  return (await response.json()).items as ApiRow[];
}

const count = (table: string) => Number(queryDatabase(`select count(*) from "${table}"`));

function trackAttempts(page: Page): Attempt[] {
  const attempts: Attempt[] = [];
  page.on('request', (request) => {
    if (request.method() !== 'POST' || new URL(request.url()).pathname !== disputesPath) return;
    const headers = request.headers();
    attempts.push({
      key: headers['idempotency-key'],
      hasAntiforgery: Boolean(headers['x-xsrf-token']),
      body: request.postDataJSON() as Record<string, unknown>,
      at: Date.now(),
    });
  });
  return attempts;
}

/** Answers each POST /api/v1/disputes from the list in order (the last step repeats). */
async function script(page: Page, steps: Step[]) {
  let call = 0;
  await page.route(`**${disputesPath}`, async (route: Route) => {
    const request = route.request();
    if (request.method() !== 'POST') return route.fallback();
    const step = steps[Math.min(call, steps.length - 1)];
    call += 1;
    if (step === 'real') return route.continue();
    if (step === 'lost-response') {
      await route.fetch();
      return route.abort('connectionreset');
    }
    if ('stub' in step) {
      const body = {
        type: step.slug ? `${problemTypeBase}${step.slug}.md` : 'about:blank',
        title: 'RAW-TITLE-MUST-NOT-SHOW',
        detail: 'RAW-DETAIL-MUST-NOT-SHOW',
        status: step.stub,
        traceId: 'qa36stubtrace0001',
        ...step.extra,
        ...(step.errors ? { errors: step.errors } : {}),
      };
      return route.fulfill({ status: step.stub, contentType: 'application/problem+json', body: JSON.stringify(body) });
    }
    const headers = { ...request.headers() };
    delete headers['content-length'];
    if (step.key) headers['idempotency-key'] = step.key;
    return route.continue({ headers, postData: JSON.stringify(step.forge(request.postDataJSON() as Record<string, unknown>)) });
  });
}

async function openFirstDispute(page: Page) {
  await page.goto('/transactions');
  const link = page.getByRole('link', { name: 'Dispute this transaction', exact: true }).first();
  await link.waitFor();
  await link.click();
  await expect(page.getByRole('heading', { name: 'Dispute this transaction' })).toBeVisible();
  return page.url().match(/\/transactions\/([^/]+)\/dispute$/)![1];
}

const chooseReason = (page: Page, label: string) => page.getByRole('radio', { name: label, exact: true }).click();
const submitButton = (page: Page) => page.getByRole('button', { name: /^(Submit dispute|Submitting…|Try again)$/ });

async function continueToReview(page: Page) {
  await page.getByRole('button', { name: 'Continue' }).click();
  await expect(page.getByRole('heading', { name: 'Review your dispute' })).toBeVisible();
}

async function draftUnrecognised(page: Page, note = 'qa 3.6') {
  const id = await openFirstDispute(page);
  await chooseReason(page, 'Unrecognised');
  if (note) await page.getByLabel('Note (optional)').fill(note);
  await continueToReview(page);
  return id;
}

async function axeViolations(page: Page) {
  const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
  return result.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join('; ')}`);
}

const plain = (text: string) => text.replace(/\s+/g, ' ');
const bodyText = async (page: Page) => plain(await page.locator('body').innerText());
const grouped = (amount: string) => {
  const [whole, cents] = amount.split('.');
  return `R${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ' ')}.${cents}`;
};
const toCents = (amount: string) => BigInt(amount.replace('.', ''));
const fromCents = (cents: bigint) => `${cents / 100n}.${String(cents % 100n).padStart(2, '0')}`;

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => {
  customerA = await signIn(browser, 'customer-a');
  customerB = await signIn(browser, 'customer-b');
});

test.afterAll(async () => {
  await customerA.close().catch(() => undefined);
  await customerB.close().catch(() => undefined);
});

test('criteria 1, 4, 5, 7: raise Unrecognised with a note: one key, antiforgery, stored event and outbox row, nothing in URL or storage, axe on each screen', async () => {
  const page = await customerA.newPage();
  const consoleErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && !knownRadixStyleViolation.test(message.text())) consoleErrors.push(message.text());
  });
  const attempts = trackAttempts(page);
  const disputesBefore = count('Disputes');
  const eventsBefore = count('StoredEvents');
  const outboxBefore = count('OutboxMessages');

  const rowsBefore = await listRows(customerA);
  const row = rowsBefore.find((candidate) => candidate.disputable)!;
  const transactionId = await openFirstDispute(page);
  expect(transactionId).toBe(row.transactionId);
  await expect(page.getByText(row.description).first()).toBeVisible();
  expect(plain(await page.locator('body').innerText())).toContain(grouped(row.amount.amount));
  expect(await axeViolations(page), '4a axe').toEqual([]);
  expect(await page.getByRole('radio').count()).toBe(6);
  await expect(page.getByLabel('Expected amount')).toHaveCount(0);

  // Continue with nothing chosen: the summary takes focus, its inner wrapper is the alert, the radiogroup names its error.
  await page.getByRole('button', { name: 'Continue' }).click();
  const summary = page.locator('[aria-labelledby="error-summary-title"]');
  await expect(summary).toBeFocused();
  await expect(summary.getByRole('alert')).toContainText('Check the details below');
  await expect(summary.getByRole('link', { name: "Choose why you're disputing this transaction." })).toBeVisible();
  const group = page.getByRole('radiogroup');
  await expect(group).toHaveAttribute('aria-invalid', 'true');
  const describedBy = await group.getAttribute('aria-describedby');
  expect(describedBy).toBe('reason-error');
  await expect(page.locator('#reason-error')).toHaveText("Choose why you're disputing this transaction.");
  expect(await axeViolations(page), '4a error state axe').toEqual([]);
  expect(attempts).toHaveLength(0);

  await chooseReason(page, 'Unrecognised');
  await page.getByLabel('Note (optional)').fill('  qa 3.6 raise  ');
  await expect(page.locator('#note-count')).toHaveText('12 / 1,000');
  await page.setViewportSize({ width: 320, height: 800 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), '4a at 320 px').toBeLessThanOrEqual(0);
  await page.setViewportSize({ width: 1280, height: 800 });
  await continueToReview(page);

  // 4b reads the draft back; the draft is nowhere in the URL, history state or storage.
  const reviewText = await bodyText(page);
  expect(reviewText).toContain(row.description);
  expect(reviewText).toContain('Unrecognised');
  expect(reviewText).toContain('qa 3.6 raise');
  expect(reviewText).toContain(grouped(row.amount.amount));
  expect(await axeViolations(page), '4b axe').toEqual([]);
  await page.setViewportSize({ width: 320, height: 800 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth), '4b at 320 px').toBeLessThanOrEqual(0);
  await page.setViewportSize({ width: 1280, height: 800 });
  const url = new URL(page.url());
  expect(url.pathname).toBe(`/transactions/${transactionId}/dispute/review`);
  expect(url.search + url.hash).toBe('');
  expect(await page.evaluate(() => JSON.stringify(history.state))).not.toMatch(/Unrecognised|unrecognised|qa 3\.6|"[0-9a-f]{8}-/);
  const storage = await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }));
  expect(storage).toEqual({ local: 0, session: 0 });

  const created = page.waitForResponse((response) => new URL(response.url()).pathname === disputesPath && response.request().method() === 'POST');
  await submitButton(page).click();
  const response = await created;
  expect(response.status()).toBe(201);
  const dispute = await response.json();
  expect(response.headers()['location']).toBe(`/api/v1/disputes/${dispute.disputeId}`);

  expect(attempts).toHaveLength(1);
  expect(attempts[0].key).toMatch(quotedUuid);
  expect(attempts[0].hasAntiforgery).toBe(true);
  expect(attempts[0].body).toEqual({
    accountRef: row.accountRef,
    transactionId: row.transactionId,
    reason: 'unrecognised',
    note: 'qa 3.6 raise',
    expectedAmount: null,
  });
  oldKey = attempts[0].key!;

  // 201: replace-navigates to /disputes/<id> (not-found until 3.7), announces, and Back never shows 4a or 4b again.
  await expect(page).toHaveURL(`${origin}/disputes/${dispute.disputeId}`);
  await expect(page.getByRole('heading', { name: "We can't find that dispute" })).toBeVisible();
  await expect(page.locator('[aria-live="polite"]')).toHaveText('Dispute submitted');
  await page.goBack();
  await expect(page).toHaveURL(`${origin}/transactions`);
  await expect(page.getByRole('heading', { name: 'Dispute this transaction' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Review your dispute' })).toHaveCount(0);

  // The list was invalidated: it now shows this transaction as disputed and offers no Dispute control for it.
  const rowsAfter = await listRows(customerA);
  const after = rowsAfter.find((candidate) => candidate.transactionId === row.transactionId)!;
  expect(after.disputable).toBe(false);
  expect(after.notDisputableReason).toBe('alreadyDisputed');
  disputedRow = after;

  // Database: one transaction wrote the dispute, its stored event and the outbox row with the same ID.
  expect(count('Disputes')).toBe(disputesBefore + 1);
  expect(count('StoredEvents')).toBe(eventsBefore + 1);
  expect(count('OutboxMessages')).toBe(outboxBefore + 1);
  const stored = queryDatabase(`select d."Status", d."Reason", d."Note", d."TransactionId", d."DisputedAmount"::text, e."EventType", e."Version", e."ActorType", o."EventType", o."Status", o."Id" = e."Id" from "Disputes" d join "StoredEvents" e on e."DisputeId" = d."Id" join "OutboxMessages" o on o."DisputeId" = d."Id" where d."Id" = '${dispute.disputeId}'`);
  expect(stored).toBe(`Submitted|Unrecognised|qa 3.6 raise|${row.transactionId}|${row.amount.amount}|DisputeRaised|1|Customer|disputedesk.dispute.raised.v1|Pending|t`);
  expect(consoleErrors).toEqual([]);
  await page.close();
});

test('criteria 2, 3: wrong amount: bounds in integer cents, live difference, string on the wire, hidden and cleared on another reason, the note count', async () => {
  const page = await customerA.newPage();
  const attempts = trackAttempts(page);
  const row = (await listRows(customerA)).find((candidate) => candidate.disputable)!;
  const amount = row.amount.amount;
  await openFirstDispute(page);

  await chooseReason(page, 'Wrong amount');
  const field = page.getByLabel('Expected amount');
  await expect(field).toBeVisible();
  await expect(field).toHaveAttribute('inputmode', 'decimal');
  const continueButton = page.getByRole('button', { name: 'Continue' });

  await continueButton.click();
  await expect(page.locator('#expectedAmount-error')).toHaveText('Enter the amount you expected to pay.');
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  await expect(field).toHaveAttribute('aria-describedby', 'expectedAmount-error');
  await expect(page.locator('[aria-labelledby="error-summary-title"]')).toBeFocused();
  expect(await axeViolations(page), 'wrong amount error axe').toEqual([]);

  for (const [input, message] of [
    ['1.234', 'Enter an amount like 80.00, with at most 2 decimals.'],
    ['abc', 'Enter an amount like 80.00, with at most 2 decimals.'],
    ['-1', 'Enter an amount like 80.00, with at most 2 decimals.'],
    [amount, `The expected amount must be less than ${grouped(amount)}.`],
  ] as const) {
    await field.fill(input);
    await field.blur();
    await expect(page.locator('#expectedAmount-error'), input).toBeVisible();
    expect(plain(await page.locator('#expectedAmount-error').innerText()), input).toBe(plain(message));
    await expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(await field.getAttribute('aria-describedby')).toContain('expectedAmount-error');
  }

  // 0.00 and one cent below the transaction amount are accepted, with the live difference below the field.
  const oneCentBelow = fromCents(toCents(amount) - 1n);
  await field.fill('0.00');
  await field.blur();
  await expect(page.locator('#expectedAmount-error')).toHaveCount(0);
  expect(plain(await page.locator('#expectedAmount-disputing').innerText())).toBe(`You're disputing ${grouped(amount)}`);
  await field.fill(oneCentBelow);
  await field.blur();
  expect(plain(await page.locator('#expectedAmount-disputing').innerText())).toBe("You're disputing R0.01");
  expect(await field.getAttribute('aria-describedby')).toBe('expectedAmount-disputing');

  // Choosing another reason hides the field and clears its value.
  await chooseReason(page, 'Duplicate charge');
  await expect(field).toHaveCount(0);
  await chooseReason(page, 'Wrong amount');
  await expect(page.getByLabel('Expected amount')).toHaveValue('');
  await page.getByLabel('Expected amount').fill(` ${oneCentBelow} `);
  await continueToReview(page);
  expect(await bodyText(page)).toContain("You're disputing R0.01");

  // Change keeps the values; continuing unchanged keeps the key; the single submit sends the amount as a string.
  await page.getByRole('link', { name: 'Change' }).click();
  await expect(page.getByLabel('Expected amount')).toHaveValue(` ${oneCentBelow} `); // kept as typed
  await expect(page.getByRole('radio', { name: 'Wrong amount', exact: true })).toBeChecked();
  await continueToReview(page);
  await submitButton(page).click();
  await expect(page).toHaveURL(/\/disputes\/[0-9a-f-]{36}$/);
  expect(attempts).toHaveLength(1);
  expect(attempts[0].body.expectedAmount).toBe(oneCentBelow);
  expect(typeof attempts[0].body.expectedAmount).toBe('string');
  expect(attempts[0].body.reason).toBe('wrongAmount');
  expect(attempts[0].body.note).toBeNull();
  const disputeId = page.url().split('/').pop();
  expect(queryDatabase(`select "Reason", "ExpectedAmount"::text, "DisputedAmount"::text, "Note" is null from "Disputes" where "Id" = '${disputeId}'`)).toBe(`WrongAmount|${oneCentBelow}|0.01|t`);
  await page.close();

  // The note count, on a fresh screen.
  const second = await customerA.newPage();
  await second.goto('/transactions');
  const stillDisputable = (await listRows(customerA)).some((candidate) => candidate.disputable);
  expect(stillDisputable, 'a disputable transaction is left for the note test (recreate the volumes if not)').toBe(true);
  await second.getByRole('link', { name: 'Dispute this transaction', exact: true }).first().click();
  const note = second.getByLabel('Note (optional)');
  const live = second.locator('[aria-live="polite"]');
  await note.fill('a'.repeat(899));
  await expect(second.locator('#note-count')).toHaveText('899 / 1,000');
  await expect(live).toHaveText('');
  await note.fill('a'.repeat(900));
  await expect(live).toHaveText('100 characters left');
  await note.fill('a'.repeat(1000));
  await expect(live).toHaveText("You've reached the limit of 1,000 characters");
  await note.fill('a'.repeat(1001));
  await expect(second.locator('#note-count')).toHaveText('1001 / 1,000');
  expect(await note.inputValue()).toHaveLength(1001);
  expect(await note.getAttribute('maxlength')).toBeNull();
  await note.fill('😀');
  await expect(second.locator('#note-count')).toHaveText('2 / 1,000');
  await note.fill(`   ${'b'.repeat(5)}   `);
  await expect(second.locator('#note-count')).toHaveText('5 / 1,000');
  await note.fill('a'.repeat(1001));
  await chooseReason(second, 'Unrecognised');
  const summaryOfSecond = second.locator('[aria-labelledby="error-summary-title"]');
  await second.getByRole('button', { name: 'Continue' }).click();
  await expect(second.locator('#note-error')).toHaveText('Your note must be 1,000 characters or fewer.');
  await expect(note).toHaveAttribute('aria-invalid', 'true');
  expect(await note.getAttribute('aria-describedby')).toContain('note-error');
  // The blur that the first press causes shows the field error; with it already showing, Continue must still put focus
  // on the error summary, never back on the Note field.
  await second.waitForTimeout(500);
  if ((await summaryOfSecond.count()) === 0) await second.getByRole('button', { name: 'Continue' }).click();
  await expect(summaryOfSecond).toBeVisible();
  await expect(summaryOfSecond).toBeFocused();
  await second.waitForTimeout(500);
  await expect(summaryOfSecond).toBeFocused();
  await expect(note).not.toBeFocused();
  expect(await second.getByRole('heading', { name: 'Review your dispute' }).count()).toBe(0);
  await second.close();
});

test('criteria 5, 6: one key per draft across every attempt and in-progress resend; Submit, Change and Cancel inert while pending; Try again; a lost answer replays', async () => {
  test.setTimeout(150_000);
  const page = await customerA.newPage();
  const attempts = trackAttempts(page);
  const disputesBefore = count('Disputes');
  const eventsBefore = count('StoredEvents');
  const outboxBefore = count('OutboxMessages');
  // 1 attempt + 5 resends of 409 idempotency-in-progress (stubbed), then 503, 500 (stubbed), a lost answer after the API
  // stored the dispute (real), then the real replay.
  await script(page, [
    ...Array<Step>(6).fill({ stub: 409, slug: 'idempotency-in-progress' }),
    { stub: 503, slug: 'core-banking-unavailable' },
    { stub: 500, slug: 'unexpected' },
    'lost-response',
    'real',
  ]);
  const transactionId = await draftUnrecognised(page, 'qa 3.6 retries');
  const submit = submitButton(page);
  const change = page.getByRole('link', { name: 'Change' });
  const cancel = page.getByRole('link', { name: 'Cancel' });

  await submit.click();
  await expect(submit).toHaveText('Submitting…');
  await expect(submit).toHaveAttribute('aria-disabled', 'true');
  await expect(submit).not.toHaveAttribute('disabled', /.*/);
  await expect(submit).toBeFocused();
  await expect(change).toHaveAttribute('aria-disabled', 'true');
  await expect(cancel).toHaveAttribute('aria-disabled', 'true');
  await submit.click({ force: true });
  await change.click({ force: true });
  await cancel.click({ force: true });
  await page.keyboard.press('Enter'); // focus is on Submit: an Enter press is ignored too
  expect(page.url()).toBe(`${origin}/transactions/${transactionId}/dispute/review`);
  expect(attempts.length, 'presses while pending sent nothing').toBe(1);

  const unconfirmed = "We couldn't confirm your dispute was received. Try again. You won't create a second dispute.";
  await expect(page.getByRole('alert').filter({ hasText: unconfirmed })).toBeVisible({ timeout: 20_000 });
  expect(attempts).toHaveLength(6);
  const gaps = attempts.slice(1).map((attempt, index) => attempt.at - attempts[index].at);
  expect(Math.min(...gaps), `resend gaps ${gaps.join(',')} ms`).toBeGreaterThanOrEqual(900);
  await expect(submit).toHaveText('Try again');
  await expect(submit).not.toHaveAttribute('aria-disabled', 'true');
  await expect(change).not.toHaveAttribute('aria-disabled', 'true');

  await submit.click(); // 503
  await expect(page.getByRole('alert').filter({ hasText: "We can't check this transaction with the bank right now. Try again." })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('qa36stubtrace0001');
  expect(await bodyText(page)).not.toContain('RAW-');
  await submit.click(); // 500
  await expect(page.getByRole('alert').filter({ hasText: 'Something went wrong on our side. Try again.' })).toBeVisible();
  await expect(page.getByRole('alert')).toContainText('qa36stubtrace0001');
  expect(await bodyText(page)).not.toContain('RAW-');
  await submit.click(); // the API stores the dispute, the browser sees a network error
  await expect(page.getByRole('alert').filter({ hasText: unconfirmed })).toBeVisible();
  expect(count('Disputes')).toBe(disputesBefore + 1);
  const replay = page.waitForResponse((response) => new URL(response.url()).pathname === disputesPath && response.request().method() === 'POST');
  await submit.click(); // the same key and body: the API replays the stored 201
  const replayed = await replay;
  expect(replayed.status()).toBe(201);
  const dispute = await replayed.json();
  await expect(page).toHaveURL(`${origin}/disputes/${dispute.disputeId}`);

  expect(attempts).toHaveLength(10);
  expect(new Set(attempts.map((attempt) => attempt.key)).size, 'one Idempotency-Key for all ten attempts').toBe(1);
  expect(attempts[0].key).toMatch(quotedUuid);
  expect(attempts.every((attempt) => attempt.hasAntiforgery), 'X-XSRF-TOKEN on every attempt').toBe(true);
  expect(new Set(attempts.map((attempt) => JSON.stringify(attempt.body))).size, 'one body for all ten attempts').toBe(1);
  expect(count('Disputes')).toBe(disputesBefore + 1);
  expect(count('StoredEvents')).toBe(eventsBefore + 1);
  expect(count('OutboxMessages')).toBe(outboxBefore + 1);
  expect(queryDatabase(`select count(*) from "StoredEvents" where "DisputeId" = '${dispute.disputeId}'`)).toBe('1');
  await page.close();
});

test('criterion 5: Change then Continue unchanged keeps the key, a different request gets a new key (stubbed 503 keeps the draft open)', async () => {
  const page = await customerA.newPage();
  const attempts = trackAttempts(page);
  const disputesBefore = count('Disputes');
  await script(page, [{ stub: 503, slug: 'core-banking-unavailable' }]);
  await draftUnrecognised(page, 'first');
  await submitButton(page).click();
  await expect(page.getByRole('alert')).toContainText("We can't check this transaction with the bank right now.");
  await page.getByRole('link', { name: 'Change' }).click();
  await continueToReview(page);
  await submitButton(page).click();
  await expect.poll(() => attempts.length).toBe(2);
  expect(attempts[1].key).toBe(attempts[0].key);
  await expect(page.getByRole('alert')).toBeVisible();
  await page.getByRole('link', { name: 'Change' }).click();
  await page.getByLabel('Note (optional)').fill('second');
  await continueToReview(page);
  await submitButton(page).click();
  await expect.poll(() => attempts.length).toBe(3);
  expect(attempts[2].key).toMatch(quotedUuid);
  expect(attempts[2].key).not.toBe(attempts[0].key);
  expect(attempts[2].body.note).toBe('second');
  expect(attempts.every((attempt) => attempt.hasAntiforgery)).toBe(true);
  expect(count('Disputes')).toBe(disputesBefore);
  await page.close();
});

test('criterion 8: real refusals from the API: already-disputed 409, not-disputable 422 (reason), not-found 404, idempotency-mismatch 422; focus to the h1, copy only, no Try again', async () => {
  const rows = await listRows(customerA);
  const notDisputable = rows.find((row) => row.notDisputableReason === 'outsideWindow') ?? rows.find((row) => row.notDisputableReason === 'notEligible');
  const customerBRow = (await listRows(customerB)).find((row) => row.disputable)!;
  expect(notDisputable, 'a not-disputable row of A exists').toBeTruthy();
  const disputesBefore = count('Disputes');
  const eventsBefore = count('StoredEvents');
  const outboxBefore = count('OutboxMessages');
  const auditBefore = count('AuditLogEntries');

  const cases: { name: string; step: Step; status: number; copy: string; extra?: string; link?: boolean }[] = [
    {
      name: 'already-disputed',
      step: { forge: (body) => ({ ...body, accountRef: disputedRow.accountRef, transactionId: disputedRow.transactionId }) },
      status: 409,
      copy: 'This transaction already has a dispute. You can only dispute a transaction once.',
      link: true,
    },
    {
      name: 'not-disputable',
      step: { forge: (body) => ({ ...body, accountRef: notDisputable!.accountRef, transactionId: notDisputable!.transactionId }) },
      status: 422,
      copy: "This transaction can't be disputed any more.",
      extra: notDisputable!.notDisputableReason === 'outsideWindow' ? "It's now older than 90 days. Contact the bank about this transaction." : 'Contact the bank about this transaction.',
    },
    {
      name: 'not-found (another customer, reached by forging the ID)',
      step: { forge: (body) => ({ ...body, accountRef: customerBRow.accountRef, transactionId: customerBRow.transactionId }) },
      status: 404,
      copy: 'This transaction is no longer available.',
    },
    {
      name: 'idempotency-mismatch (the first raise key, another body)',
      step: { forge: (body) => body, key: oldKey },
      status: 422,
      copy: 'Something went wrong on our side. Try again.',
    },
  ];

  for (const refusal of cases) {
    const page = await customerA.newPage();
    const attempts = trackAttempts(page);
    await script(page, [refusal.step]);
    await draftUnrecognised(page, `refusal ${refusal.name}`.slice(0, 40));
    const listRequests: string[] = [];
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/v1/transactions') listRequests.push(request.url());
    });
    const answered = page.waitForResponse((response) => new URL(response.url()).pathname === disputesPath && response.request().method() === 'POST');
    await submitButton(page).click();
    const response = await answered;
    expect(response.status(), refusal.name).toBe(refusal.status);
    const problem = await response.json();
    const alert = page.getByRole('alert').filter({ hasText: refusal.copy });
    await expect(alert, refusal.name).toBeVisible();
    if (refusal.extra) await expect(alert).toContainText(refusal.extra);
    // The reference is the problem's traceId; nothing from the body's title or detail is shown.
    await expect(alert).toContainText(problem.traceId);
    expect(await bodyText(page), refusal.name).not.toContain(problem.detail ?? '\u0000');
    expect(await bodyText(page), refusal.name).not.toContain(problem.title ?? '\u0000');
    await expect(page.getByRole('heading', { name: 'Review your dispute', level: 1 }), refusal.name).toBeFocused();
    await expect(page.getByRole('button', { name: 'Try again' }), refusal.name).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Submit dispute' }), refusal.name).toHaveCount(0);
    await expect(page.getByRole('link', { name: 'Back to transactions' }), refusal.name).toBeVisible();
    if (refusal.link) {
      expect(problem.disputeId).toBe(disputedRow && queryDatabase(`select "Id" from "Disputes" where "TransactionId" = '${disputedRow.transactionId}' and "AccountRef" = '${disputedRow.accountRef}'`));
      await expect(page.getByRole('link', { name: 'View dispute' })).toHaveAttribute('href', `/disputes/${problem.disputeId}`);
    }
    expect(await axeViolations(page), `${refusal.name} axe`).toEqual([]);
    // 409 and 422 not-disputable invalidate the list query. It is not mounted on 4b, so the refetch happens when the
    // customer goes back: the list is requested again then.
    const requestsOnNotice = listRequests.length;
    await page.getByRole('link', { name: 'Back to transactions' }).click();
    await expect(page).toHaveURL(`${origin}/transactions`);
    await expect.poll(() => listRequests.length, `${refusal.name}: list requested again on return`).toBeGreaterThan(requestsOnNotice);
    expect(attempts, refusal.name).toHaveLength(1);
    expect(attempts[0].hasAntiforgery).toBe(true);
    await page.close();
  }

  expect(count('Disputes')).toBe(disputesBefore);
  expect(count('StoredEvents')).toBe(eventsBefore);
  expect(count('OutboxMessages')).toBe(outboxBefore);
  console.log(`audit rows written by the four refusals: ${count('AuditLogEntries') - auditBefore}`);
});

test('criterion 4: a 400 returns to 4a with the values kept, each field its own copy, the summary takes focus (stubbed 400s)', async () => {
  const row = (await listRows(customerA)).find((candidate) => candidate.disputable)!;
  const amount = row.amount.amount;
  const page = await customerA.newPage();
  const attempts = trackAttempts(page);
  const summary = page.locator('[aria-labelledby="error-summary-title"]');
  const formError = "We couldn't check your dispute. Review the details and try again.";

  const cases: { name: string; step: Step; fieldError?: [string, string]; formLevel: boolean }[] = [
    {
      name: 'errors.expectedAmount',
      step: { stub: 400, slug: 'validation', errors: { expectedAmount: ['RAW-ERROR-TEXT-MUST-NOT-SHOW'] } },
      fieldError: ['#expectedAmount-error', `Enter an amount less than ${grouped(amount)}, with at most 2 decimals.`],
      formLevel: false,
    },
    { name: 'errors.note', step: { stub: 400, slug: 'validation', errors: { note: ['x'] } }, fieldError: ['#note-error', 'Your note must be 1,000 characters or fewer.'], formLevel: false },
    { name: 'unknown errors key', step: { stub: 400, slug: 'validation', errors: { somethingElse: ['x'] } }, formLevel: true },
    { name: 'no errors (a body binding failure)', step: { stub: 400, slug: 'validation' }, formLevel: true },
  ];

  for (const scenario of cases) {
    const fresh = page;
    await fresh.unroute(`**${disputesPath}`).catch(() => undefined);
    await script(fresh, [scenario.step]);
    await fresh.goto('/transactions');
    await fresh.getByRole('link', { name: 'Dispute this transaction', exact: true }).first().click();
    await chooseReason(fresh, 'Wrong amount');
    await fresh.getByLabel('Expected amount').fill('0.01');
    await fresh.getByLabel('Note (optional)').fill('keep me');
    await continueToReview(fresh);
    await submitButton(fresh).click();
    await expect(fresh.getByRole('heading', { name: 'Dispute this transaction' }), scenario.name).toBeVisible();
    expect(fresh.url(), scenario.name).toMatch(/\/dispute$/);
    await expect(summary, scenario.name).toBeFocused();
    await expect(summary.getByRole('alert'), scenario.name).toContainText('Check the details below');
    await expect(fresh.getByRole('radio', { name: 'Wrong amount', exact: true })).toBeChecked();
    await expect(fresh.getByLabel('Expected amount')).toHaveValue('0.01');
    await expect(fresh.getByLabel('Note (optional)')).toHaveValue('keep me');
    if (scenario.fieldError) {
      const [selector, copy] = scenario.fieldError;
      expect(plain(await fresh.locator(selector).innerText()), scenario.name).toBe(plain(copy));
      const fieldId = selector.replace('#', '').replace('-error', '');
      const control = fresh.locator(`#${fieldId}`);
      await expect(control).toHaveAttribute('aria-invalid', 'true');
      expect(await control.getAttribute('aria-describedby')).toContain(selector.slice(1));
    }
    if (scenario.formLevel) await expect(summary).toContainText(formError);
    else await expect(summary).not.toContainText(formError);
    expect(await bodyText(fresh), scenario.name).not.toContain('RAW-');
    expect(await axeViolations(fresh), `${scenario.name} axe`).toEqual([]);
    expect(attempts.at(-1)!.hasAntiforgery).toBe(true);
  }
  await page.close();
});

test('criterion 1: deep link, reload, not-disputable and unknown ID all land on /transactions; a reload loses the draft', async () => {
  const rows = await listRows(customerA);
  const disputable = rows.find((row) => row.disputable)!;
  const blocked = rows.find((row) => !row.disputable)!;
  const page = await customerA.newPage();

  for (const path of [
    `/transactions/${disputable.transactionId}/dispute`,
    `/transactions/${disputable.transactionId}/dispute/review`,
    `/transactions/${blocked.transactionId}/dispute`,
    '/transactions/does-not-exist/dispute',
  ]) {
    await page.goto(path);
    await expect(page, path).toHaveURL(`${origin}/transactions`);
    await expect(page.getByRole('heading', { name: 'Dispute this transaction' })).toHaveCount(0);
  }

  // A not-disputable row has no Dispute control; a disputable one has a link (no full page load).
  await page.goto('/transactions');
  await page.getByRole('link', { name: 'Dispute this transaction', exact: true }).first().waitFor();
  const navigations: string[] = [];
  page.on('framenavigated', (frame) => navigations.push(frame.url()));
  await page.evaluate(() => { (window as unknown as { marker: string }).marker = 'same-document'; });
  await page.getByRole('link', { name: 'Dispute this transaction', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: 'Dispute this transaction' })).toBeVisible();
  expect(await page.evaluate(() => (window as unknown as { marker?: string }).marker)).toBe('same-document');

  // Reload on 4b loses the draft and the cached row: back to the list.
  await chooseReason(page, 'Unrecognised');
  await continueToReview(page);
  await page.reload();
  await expect(page).toHaveURL(`${origin}/transactions`);
  await page.close();
});

test('criterion 8: a 401 on 4b goes to the session-ended screen and Sign in again carries returnUrl=/transactions only', async ({ browser }) => {
  const context = await signIn(browser, 'customer-a');
  const page = await context.newPage();
  const attempts = trackAttempts(page);
  const transactionId = await draftUnrecognised(page, 'qa 3.6 session');
  const outside = await playwrightRequest.newContext({ baseURL: origin, storageState: await context.storageState() });
  const token = (await (await outside.get('/api/v1/bff/antiforgery')).json()).requestToken;
  expect((await outside.post('/api/v1/bff/logout', { form: { __RequestVerificationToken: token }, maxRedirects: 0 })).status()).toBe(302);
  await outside.dispose();

  const disputesBefore = count('Disputes');
  const antiforgeryStatuses: number[] = [];
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/api/v1/bff/antiforgery') antiforgeryStatuses.push(response.status());
  });
  await submitButton(page).click();
  await expect(page.getByRole('heading', { name: "You've been signed out" })).toBeVisible();
  // customFetch fetches the request token first; with the session gone that is the 401, so no POST is sent at all.
  expect(antiforgeryStatuses).toEqual([401]);
  expect(attempts).toHaveLength(0);
  const href = await page.getByRole('link', { name: 'Sign in again' }).getAttribute('href');
  expect(href).toBe('/api/v1/bff/login?returnUrl=%2Ftransactions');
  expect(href).not.toContain(transactionId);
  expect(page.url()).not.toContain(transactionId);
  expect(count('Disputes')).toBe(disputesBefore);
  await context.close();
});

// Story 3.11: the raise screens emit browser spans that join the API trace. The browser's OTLP/HTTP exports to the
// gateway's /otlp/ are read from the wire, and the trace is read back from the Aspire Dashboard's telemetry API.
const dashboardOrigin = process.env.DASHBOARD_ORIGIN ?? 'http://localhost:18888';
const traceparentPattern = /^00-([0-9a-f]{32})-([0-9a-f]{16})-0[01]$/;

type OtlpValue = { stringValue?: string; intValue?: string | number; boolValue?: boolean };
type OtlpSpan = {
  traceId: string;
  spanId: string;
  parentSpanId?: string;
  name: string;
  links?: { traceId: string; spanId: string; attributes?: unknown[] }[];
  events?: unknown[];
  attributes?: { key: string; value: OtlpValue }[];
};
type OtlpExport = { resourceSpans: { resource: { attributes?: { key: string }[] }; scopeSpans: { spans: OtlpSpan[] }[] }[] };

const spansOf = (exports: OtlpExport[]) => exports.flatMap((e) => e.resourceSpans.flatMap((r) => r.scopeSpans.flatMap((s) => s.spans)));

test('story 3.11 criteria 1-5: one trace from dispute.raise.submit to the API POST, open span and link, attribute keys, no personal data, same-origin export, no CSP violation', async ({ browser }) => {
  test.setTimeout(120_000);
  const context = await signIn(browser, 'customer-a');
  const page = await context.newPage();
  await page.addInitScript(() => {
    (window as unknown as { cspViolations: string[] }).cspViolations = [];
    document.addEventListener('securitypolicyviolation', (event) => {
      (window as unknown as { cspViolations: string[] }).cspViolations.push(`${event.violatedDirective} ${event.blockedURI}`);
    });
  });
  const consoleErrors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error' && !knownRadixStyleViolation.test(message.text())) consoleErrors.push(message.text());
  });

  // Every request the page makes: its origin, path, traceparent and baggage headers (set by script, so request.headers()
  // holds them); every OTLP export and its answer.
  const requests: { origin: string; path: string; method: string; traceparent?: string; baggage?: string; key?: string }[] = [];
  const exports: OtlpExport[] = [];
  const exportStatuses: number[] = [];
  const exportHeaders: Record<string, string>[] = [];
  page.on('request', (request) => {
    const headers = request.headers();
    const url = new URL(request.url());
    requests.push({ origin: url.origin, path: url.pathname, method: request.method(), traceparent: headers['traceparent'], baggage: headers['baggage'], key: headers['idempotency-key'] });
    if (url.pathname === '/otlp/v1/traces' && request.method() === 'POST') {
      exports.push(request.postDataJSON() as OtlpExport);
      exportHeaders.push(headers);
    }
  });
  page.on('response', (response) => {
    if (new URL(response.url()).pathname === '/otlp/v1/traces') exportStatuses.push(response.status());
  });

  const document = await page.goto('/transactions');
  expect(document!.headers()['content-security-policy']).toBe(gatewayContentSecurityPolicy);
  const row = (await listRows(context)).find((candidate) => candidate.disputable)!;
  const attempts = trackAttempts(page);
  const transactionId = await openFirstDispute(page);
  expect(transactionId).toBe(row.transactionId);
  await chooseReason(page, 'Unrecognised');
  await page.getByLabel('Note (optional)').fill('qa 3.11 trace note');
  await continueToReview(page);
  // Back to 4a and on to 4b again: moving inside the flow records no second open span.
  await page.getByRole('link', { name: 'Change' }).click();
  await expect(page.getByRole('heading', { name: 'Dispute this transaction' })).toBeVisible();
  await continueToReview(page);
  const created = page.waitForResponse((response) => new URL(response.url()).pathname === disputesPath && response.request().method() === 'POST');
  await submitButton(page).click();
  const response = await created;
  expect(response.status()).toBe(201);
  const dispute = await response.json();
  await expect(page).toHaveURL(`${origin}/disputes/${dispute.disputeId}`);
  const customerId = queryDatabase(`select "CustomerId" from "Disputes" where "Id" = '${dispute.disputeId}'`);

  // The 5 s batch timer exports the spans; wait for the export that holds the submit span.
  await expect.poll(() => spansOf(exports).some((span) => span.name === 'dispute.raise.submit'), { timeout: 30_000 }).toBe(true);

  // Criterion 7 (CSP) and 5 (same-origin export): no violation, no console error, every export answered 200 at the gateway's /otlp/.
  expect(await page.evaluate(() => (window as unknown as { cspViolations: string[] }).cspViolations)).toEqual([]);
  expect(consoleErrors).toEqual([]);
  expect(exportStatuses.length).toBeGreaterThan(0);
  expect(exportStatuses.every((status) => status === 200)).toBe(true);
  expect(exportHeaders.every((headers) => headers['content-type'] === 'application/json')).toBe(true);
  expect(requests.filter((r) => r.path.startsWith('/otlp/')).every((r) => r.origin === origin && r.path === '/otlp/v1/traces')).toBe(true);

  // Criterion 1: traceparent on same-origin /api calls, never a baggage header anywhere, and none on the OTLP exports.
  const apiCalls = requests.filter((r) => r.path.startsWith('/api/'));
  expect(apiCalls.length).toBeGreaterThan(0);
  expect(apiCalls.every((r) => r.origin === origin && traceparentPattern.test(r.traceparent ?? ''))).toBe(true);
  expect(requests.every((r) => r.baggage === undefined)).toBe(true);
  expect(requests.filter((r) => r.path.startsWith('/otlp/')).every((r) => r.traceparent === undefined)).toBe(true);

  const spans = spansOf(exports);
  const opens = spans.filter((span) => span.name === 'dispute.raise.open');
  const submits = spans.filter((span) => span.name === 'dispute.raise.submit');
  // Criterion 3: one open for the flow (4a, 4b, Change and Continue), one submit linked to it, ended with the status and outcome.
  expect(opens).toHaveLength(1);
  expect(submits).toHaveLength(1);
  const [open] = opens;
  const [submit] = submits;
  expect(open.attributes ?? []).toEqual([]);
  expect(submit.links).toHaveLength(1);
  expect(submit.links![0].spanId).toBe(open.spanId);
  expect(submit.links![0].traceId).toBe(open.traceId);
  expect(submit.links![0].attributes ?? []).toEqual([]);
  expect((submit.attributes ?? []).map((a) => a.key).sort()).toEqual(['dispute.raise.outcome', 'http.response.status_code']);
  const attribute = (key: string) => submit.attributes!.find((a) => a.key === key)!.value;
  expect(attribute('dispute.raise.outcome').stringValue).toBe('submitted');
  expect(Number(attribute('http.response.status_code').intValue)).toBe(201);
  expect(open.traceId).not.toBe(submit.traceId);

  // Criterion 2: the POST's traceparent is the submit trace, and its parent chain is submit -> fetch span -> POST.
  const post = requests.find((r) => r.method === 'POST' && r.path === disputesPath)!;
  expect(attempts).toHaveLength(1);
  const [, postTraceId, postSpanId] = traceparentPattern.exec(post.traceparent!)!;
  expect(postTraceId).toBe(submit.traceId);
  const fetchSpan = spans.find((span) => span.spanId === postSpanId)!;
  expect(fetchSpan.parentSpanId).toBe(submit.spanId);
  // The antiforgery fetch before the POST is in the same trace.
  const antiforgery = requests.filter((r) => r.path === '/api/v1/bff/antiforgery').map((r) => traceparentPattern.exec(r.traceparent!)![1]);
  expect(antiforgery).toContain(submit.traceId);

  // Criterion 4: no personal data anywhere in what was exported, and the resource names only the service.
  const wire = JSON.stringify(exports);
  for (const secret of [row.transactionId, row.accountRef, row.amount.amount, 'qa 3.11 trace note', post.key!, dispute.disputeId, customerId]) {
    expect(secret.length).toBeGreaterThan(3);
    expect(wire, 'an exported span holds a value that must not be there').not.toContain(secret);
  }
  for (const span of spans) {
    for (const attributeEntry of span.attributes ?? []) {
      const value = String(attributeEntry.value.stringValue ?? '');
      if (attributeEntry.key === 'url.full') expect(value).not.toContain('?');
    }
  }
  const resourceKeys = exports.flatMap((e) => e.resourceSpans.flatMap((r) => (r.resource.attributes ?? []).map((a) => a.key))).sort();
  expect([...new Set(resourceKeys)]).toEqual(['service.name', 'telemetry.sdk.language', 'telemetry.sdk.name', 'telemetry.sdk.version']);

  // The Aspire Dashboard holds one trace: the browser's submit span, the fetch span and the API's server span for the POST.
  await expect.poll(async () => {
    const body = await (await fetch(`${dashboardOrigin}/api/telemetry/traces?traceId=${submit.traceId}`)).json();
    const inTrace = (body.data.resourceSpans as { scopeSpans: { spans: { traceId: string; name: string; kind: number }[] }[] }[])
      .flatMap((r) => r.scopeSpans.flatMap((s) => s.spans)).filter((span) => span.traceId === submit.traceId);
    const names = inTrace.map((span) => span.name);
    return names.includes('dispute.raise.submit') && inTrace.some((span) => span.kind === 2 && /POST/.test(span.name));
  }, { timeout: 30_000 }).toBe(true);
  await context.close();
});

test('story 3.11 criteria 1, 4: a fetch to another origin carries no traceparent and no baggage; a cursor in a same-origin URL never reaches the export', async ({ browser }) => {
  const context = await signIn(browser, 'customer-a');
  const page = await context.newPage();
  // The gateway's CSP would block the cross-origin fetch before it is sent, so this page is served without it.
  await page.route(`${origin}/transactions`, async (route) => {
    const response = await route.fetch();
    const headers = { ...response.headers() };
    delete headers['content-security-policy'];
    await route.fulfill({ response, headers });
  });
  let crossOriginHeaders: Record<string, string> | undefined;
  await page.route('http://other-origin.test/**', async (route) => {
    crossOriginHeaders = await route.request().allHeaders();
    await route.fulfill({ status: 200, headers: { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*' }, body: 'ok' });
  });
  await page.goto('/transactions');
  await expect(page.getByRole('heading', { name: 'Transactions' }).first()).toBeVisible();
  const sameOrigin = page.waitForRequest((request) => new URL(request.url()).pathname === '/api/v1/bff/antiforgery');
  const exported: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/otlp/v1/traces') exported.push(request.postData() ?? '');
  });
  await page.evaluate(async () => {
    await fetch('http://other-origin.test/ping');
    await fetch('/api/v1/bff/antiforgery');
    await fetch('/api/v1/transactions?limit=1&cursor=QA-SECRET-CURSOR');
  });
  await expect.poll(() => exported.some((body) => body.includes('/api/v1/transactions')), { timeout: 30_000 }).toBe(true);
  expect(exported.join('')).not.toContain('QA-SECRET-CURSOR');
  expect(exported.join('')).not.toContain('cursor');
  expect((await (await sameOrigin).allHeaders())['traceparent']).toMatch(traceparentPattern);
  expect(crossOriginHeaders).toBeDefined();
  expect(crossOriginHeaders!['traceparent']).toBeUndefined();
  expect(crossOriginHeaders!['baggage']).toBeUndefined();
  await context.close();
});

test('story 3.11 criteria 2, 3, 4: a refused raise (404 not-found, forged to another customer\'s transaction) ends the submit span with the status and the problem slug, and holds no id', async ({ browser }) => {
  const context = await signIn(browser, 'customer-a');
  const page = await context.newPage();
  const customerBRow = (await listRows(customerB)).find((row) => row.disputable)!;
  const exports: OtlpExport[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/otlp/v1/traces') exports.push(request.postDataJSON() as OtlpExport);
  });
  await script(page, [{ forge: (body) => ({ ...body, accountRef: customerBRow.accountRef, transactionId: customerBRow.transactionId }) }]);
  await draftUnrecognised(page, 'qa 3.11 refusal');
  const refused = page.waitForResponse((response) => new URL(response.url()).pathname === disputesPath && response.request().method() === 'POST');
  await submitButton(page).click();
  expect((await refused).status()).toBe(404);
  await expect.poll(() => spansOf(exports).some((span) => span.name === 'dispute.raise.submit'), { timeout: 30_000 }).toBe(true);

  const spans = spansOf(exports);
  const open = spans.find((span) => span.name === 'dispute.raise.open')!;
  const submit = spans.find((span) => span.name === 'dispute.raise.submit')!;
  expect(submit.links![0].spanId).toBe(open.spanId);
  const attributes = Object.fromEntries(submit.attributes!.map((a) => [a.key, a.value]));
  expect(Object.keys(attributes).sort()).toEqual(['dispute.raise.outcome', 'http.response.status_code']);
  expect(attributes['dispute.raise.outcome'].stringValue).toBe('not-found');
  expect(Number(attributes['http.response.status_code'].intValue)).toBe(404);
  const wire = JSON.stringify(exports);
  for (const secret of [customerBRow.transactionId, customerBRow.accountRef, 'qa 3.11 refusal']) expect(wire).not.toContain(secret);
  await context.close();
});
