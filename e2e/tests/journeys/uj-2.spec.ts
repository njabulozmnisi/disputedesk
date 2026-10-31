import { expect, test, type BrowserContext, type Page } from '@playwright/test';
import { expectProblemShape, fixtureLogin, journeySeededDisputes, pause, queryRows, signInContext, topicRecords } from '../support';
import { expectAccessibleAtBothWidths, focusSummary } from './screens';

// UJ-2 (story 7.2, criteria 1, 2 and 6): customer A withdraws the seeded Under review dispute on TXN-A-0022 through the
// real dialog, traced browser -> network -> database (qa_readonly) -> topic. Run on a fresh stack: the dispute is withdrawn for real.

const seeded = journeySeededDisputes.withdrawnByUj2;
const note = 'Parcel arrived';

let context: BrowserContext;

test.beforeAll(async ({ browser }) => {
  context = await signInContext(browser, 'customer-a');
});
test.afterAll(async () => context.close());

type Dispute = { Id: string; Status: string; Version: number; CustomerId: string };
const disputeRow = () => queryRows<Dispute>(`select "Id", "Status", "Version", "CustomerId" from "Disputes" where "AccountRef" = '${seeded.accountRef}' and "TransactionId" = '${seeded.transactionId}'`)[0];
const events = (id: string) => queryRows<{ Id: string; Version: number; EventType: string; Note: string | null }>(`select "Id", "Version", "EventType", "Data"->>'note' as "Note" from "StoredEvents" where "DisputeId" = '${id}' order by "Version"`);
const outbox = (id: string) => queryRows<{ Id: string; Status: string; EventType: string }>(`select "Id", "Status", "EventType" from "OutboxMessages" where "DisputeId" = '${id}'`);
type RaiseRejection = { ActorType: string; ActorId: string };
const raiseRejections = (customerId: string) => queryRows<RaiseRejection>(
  `select "ActorType", "ActorId" from "AuditLogEntries" where "Action" = 'Raise' and "Result" = 'Rejected' and "CustomerId" = '${customerId}' order by "OccurredAtUtc"`);

async function openTransactionsRow(page: Page, disputeId: string) {
  await page.goto('/transactions');
  const badge = page.locator(`a[href="/disputes/${disputeId}"]`);
  for (let loads = 0; loads < 8 && (await badge.count()) === 0; loads++) {
    await page.getByRole('button', { name: 'Load more' }).click();
    await page.getByRole('button', { name: 'Load more' }).or(badge).first().waitFor();
  }
  return page.locator('li').filter({ has: badge });
}

test('UJ-2: A withdraws the Under review dispute on TXN-A-0022', async () => {
  const before = disputeRow();
  expect(before, 'the seeded dispute exists (fresh stack)').toBeTruthy();
  expect(before.Status, 'fresh stack: the dispute is still Under review').toBe('UnderReview');
  const eventsBefore = events(before.Id);
  const outboxBefore = outbox(before.Id);
  const auditBefore = raiseRejections(before.CustomerId).length;

  const page = await context.newPage();
  await page.goto(`/disputes/${before.Id}`);
  await expect(page.getByText(/^Dispute status:\s*Under review$/)).toBeVisible();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Spotify');
  await expectAccessibleAtBothWidths(page, 'Under review detail');

  // Keyboard pass on the dialog: opened from the keyboard, focus inside, Escape returns it to the Withdraw control.
  const withdrawButton = page.getByRole('button', { name: 'Withdraw dispute' });
  await withdrawButton.focus();
  await page.keyboard.press('Enter');
  const dialog = page.getByRole('alertdialog', { name: 'Withdraw this dispute?' });
  await expect(dialog).toBeVisible();
  expect((await focusSummary(page)).inDialog).toBe(true);
  for (let presses = 0; presses < 6; presses++) {
    await page.keyboard.press('Tab');
    expect((await focusSummary(page)).inDialog, 'Tab never leaves the dialog').toBe(true);
  }
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  // Radix restores focus just after it unmounts the dialog, so wait for it instead of reading it in the same instant.
  await expect.poll(async () => (await focusSummary(page)).text, { message: 'focus returns to the control that opened the dialog', timeout: 3_000 }).toBe('Withdraw dispute');

  await withdrawButton.click();
  await expect(dialog).toBeVisible();
  await expectAccessibleAtBothWidths(page, 'Withdraw dialog open');
  await page.getByLabel('Note (optional)').fill(note);
  await expectAccessibleAtBothWidths(page, 'Withdraw dialog with the note');

  const withdrawResponse = page.waitForResponse((response) => response.request().method() === 'POST' && response.url().endsWith(`/api/v1/disputes/${before.Id}/withdraw`));
  await dialog.getByRole('button', { name: 'Withdraw dispute' }).click();
  const withdraw = await withdrawResponse;
  expect(withdraw.status()).toBe(200);
  expect(await withdraw.request().headerValue('x-xsrf-token')).toBeTruthy();
  const responseBody = await withdraw.json();
  expect(responseBody.disputeId).toBe(before.Id);
  expect(JSON.stringify(responseBody)).not.toMatch(/CUS-|worker:|DisputeWithdrawn|"customerId"/);

  // Browser: the detail now reads Withdrawn, no Withdraw control, the timeline ends with the customer's own note.
  await expect(page.getByText(/^Dispute status:\s*Withdrawn$/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
  const last = page.getByRole('region', { name: 'Timeline' }).getByRole('listitem').last();
  await expect(last).toContainText('Withdrawn');
  await expect(last).toContainText(`“${note}”`);
  await expect(last).toContainText('You ·');
  await expectAccessibleAtBothWidths(page, 'Withdrawn detail');

  // Database: one version up, one new stored event holding the note, one new outbox row with the same Id.
  const after = disputeRow();
  expect(after.Status).toBe('Withdrawn');
  expect(after.Version).toBe(before.Version + 1);
  const eventsAfter = events(before.Id);
  expect(eventsAfter).toHaveLength(eventsBefore.length + 1);
  const newEvent = eventsAfter[eventsAfter.length - 1];
  expect(newEvent).toMatchObject({ EventType: 'DisputeWithdrawn', Version: after.Version, Note: note });
  const outboxAfter = outbox(before.Id);
  expect(outboxAfter).toHaveLength(outboxBefore.length + 1);
  expect(outboxAfter.filter((row) => row.Id === newEvent.Id)).toHaveLength(1);
  await expect.poll(() => outbox(before.Id).find((row) => row.Id === newEvent.Id)?.Status, { timeout: 30_000 }).toBe('Sent');

  // Topic: keyed by the dispute, the status-changed event with the new stored event's Id.
  await expect.poll(() => topicRecords().filter((record) => record.key === before.Id && JSON.parse(record.value).id === newEvent.Id).length, { timeout: 30_000 }).toBeGreaterThan(0);
  const records = topicRecords().filter((record) => JSON.parse(record.value).id === newEvent.Id);
  for (const record of records) {
    const envelope = JSON.parse(record.value);
    expect(record.key).toBe(before.Id);
    expect(envelope.type).toBe('disputedesk.dispute.status-changed.v1');
    expect(envelope.data).toMatchObject({ disputeId: before.Id, fromStatus: 'underReview', toStatus: 'withdrawn', action: 'withdraw', actorType: 'customer' });
    expect(record.headers.map((header) => header.key)).toContain('traceparent');
    expect(records[0].value).toBe(record.value); // at-least-once: every copy is the same event
  }

  // /transactions: the row shows Withdrawn and no Dispute control; the API says the same and refuses a second raise.
  const row = await openTransactionsRow(page, before.Id);
  await expect(row.getByRole('link', { name: 'Dispute status: Withdrawn' })).toBeVisible();
  await expect(row.getByRole('link', { name: 'Dispute this transaction' })).toHaveCount(0);
  await expectAccessibleAtBothWidths(page, 'Transactions with the Withdrawn row');

  const listed = await listAll();
  expect(listed.find((item) => item.transactionId === seeded.transactionId)).toMatchObject({ disputable: false });

  const token = (await (await context.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const raise = await context.request.post('/api/v1/disputes', {
    headers: { 'X-XSRF-TOKEN': token, 'Idempotency-Key': `"uj2-${Date.now()}"` },
    data: { accountRef: seeded.accountRef, transactionId: seeded.transactionId, reason: 'unrecognised' },
    maxRedirects: 0,
  });
  expect(raise.status()).toBe(409);
  const problem = await raise.json();
  expect(expectProblemShape(problem, 'already-disputed', 409)).toEqual([]);
  expect(problem.disputeId).toBe(before.Id);
  const rejections = raiseRejections(before.CustomerId);
  expect(rejections).toHaveLength(auditBefore + 1);
  expect(rejections[rejections.length - 1]).toMatchObject({ ActorType: 'Customer', ActorId: fixtureLogin('customer-a').userId });
  await page.close();
});

async function listAll() {
  const items: { transactionId: string; disputable: boolean }[] = [];
  let cursor: string | null = null;
  do {
    await pause(150); // the gateway's coarse rate limit
    const response = await context.request.get(`/api/v1/transactions?limit=50${cursor === null ? '' : `&cursor=${cursor}`}`);
    expect(response.status()).toBe(200);
    const page = (await response.json()) as { items: typeof items; nextCursor: string | null };
    items.push(...page.items);
    cursor = page.nextCursor;
  } while (cursor !== null);
  return items;
}
