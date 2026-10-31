import { expect, test, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, journeySeededDisputes, queryRows, signInContext } from '../support';
import { expectAccessibleAtBothWidths } from './screens';

// UJ-5b (story 7.2, criteria 4 and 6): A's Resolved (Upheld) dispute on TXN-A-0015 cannot be withdrawn. 409
// action-not-allowed, nothing changes, one Withdraw Rejected audit row (AD-14a). Safe to repeat: it adds only audit rows.

const seeded = journeySeededDisputes.resolvedUj5b;
let context: BrowserContext;

test.beforeAll(async ({ browser }) => {
  context = await signInContext(browser, 'customer-a');
});
test.afterAll(async () => context.close());

type State = { status: string; version: number; stored: number; outbox: number };
const state = (id: string) => queryRows<State>(
  `select (select "Status" from "Disputes" where "Id" = '${id}') as status,
          (select "Version" from "Disputes" where "Id" = '${id}') as version,
          (select count(*)::int from "StoredEvents" where "DisputeId" = '${id}') as stored,
          (select count(*)::int from "OutboxMessages" where "DisputeId" = '${id}') as outbox`)[0];
const audit = (id: string) => queryRows<{ Action: string; Result: string; ActorType: string; ActorId: string }>(
  `select "Action", "Result", "ActorType", "ActorId" from "AuditLogEntries" where "DisputeId" = '${id}' and "Action" = 'Withdraw' and "Result" = 'Rejected' order by "OccurredAtUtc"`);

test('UJ-5b: a resolved dispute cannot be withdrawn', async () => {
  const [dispute] = queryRows<{ Id: string; Status: string; Outcome: string }>(
    `select "Id", "Status", "Outcome" from "Disputes" where "AccountRef" = '${seeded.accountRef}' and "TransactionId" = '${seeded.transactionId}'`);
  expect(dispute.Status).toBe('Resolved');
  expect(dispute.Outcome).toBe('Upheld');
  const before = state(dispute.Id);
  const auditBefore = audit(dispute.Id).length;

  const page = await context.newPage();
  await page.goto(`/disputes/${dispute.Id}`);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Clicks Menlyn');
  await expect(page.getByText(/^Dispute status:\s*Resolved · Upheld$/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);
  await expectAccessibleAtBothWidths(page, 'Resolved detail');

  const token = (await (await context.request.get('/api/v1/bff/antiforgery')).json()).requestToken as string;
  const response = await context.request.post(`/api/v1/disputes/${dispute.Id}/withdraw`, {
    headers: { 'X-XSRF-TOKEN': token, 'Content-Type': 'application/json' },
    data: '{}',
    maxRedirects: 0,
  });
  expect(response.status()).toBe(409);
  const body = await response.json();
  expect(expectProblemShape(body, 'action-not-allowed', 409)).toEqual([]);
  expect(body.customerStatus).toBe('resolved');

  expect(state(dispute.Id), 'status, version, stored events and outbox rows are unchanged').toEqual(before);
  const entries = audit(dispute.Id);
  expect(entries).toHaveLength(auditBefore + 1);
  expect(entries[entries.length - 1]).toMatchObject({ Action: 'Withdraw', Result: 'Rejected', ActorType: 'Customer', ActorId: fixtureLogin('customer-a').userId });
  await page.close();
});
