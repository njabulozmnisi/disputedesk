import { randomUUID } from 'node:crypto';
import { expect, test, type BrowserContext } from '@playwright/test';
import { expectProblemShape, fixtureLogin, journeySeededDisputes, pause, queryDatabase, queryRows, signInContext, withoutTraceId } from '../support';
import { expectAccessibleAtBothWidths } from './screens';

// UJ-5a (story 7.2, criteria 3 and 6): A opens B's dispute on TXN-B-0025. The answer is the one not-found, exactly the
// unknown-ID answer, and the read writes nothing. Read-only journey: safe on any stack.

const seeded = journeySeededDisputes.otherCustomersUj5a;
let contextA: BrowserContext;
let contextB: BrowserContext;

test.beforeAll(async ({ browser }) => {
  contextA = await signInContext(browser, 'customer-a');
  contextB = await signInContext(browser, 'customer-b');
});
test.afterAll(async () => {
  await contextA.close();
  await contextB.close();
});

const counts = (id: string) => queryRows<{ version: number; stored: number; outbox: number; auditForDispute: number; auditTotal: number }>(
  `select (select "Version" from "Disputes" where "Id" = '${id}') as version,
          (select count(*)::int from "StoredEvents" where "DisputeId" = '${id}') as stored,
          (select count(*)::int from "OutboxMessages" where "DisputeId" = '${id}') as outbox,
          (select count(*)::int from "AuditLogEntries" where "DisputeId" = '${id}') as "auditForDispute",
          (select count(*)::int from "AuditLogEntries") as "auditTotal"`)[0];

test("UJ-5a: B's dispute is not found for A, exactly like an unknown ID", async () => {
  const [dispute] = queryRows<{ Id: string; CustomerId: string; Status: string }>(
    `select "Id", "CustomerId", "Status" from "Disputes" where "AccountRef" = '${seeded.accountRef}' and "TransactionId" = '${seeded.transactionId}'`);
  expect(dispute.Status).toBe('Submitted');
  const [owner] = queryRows<{ CustomerId: string }>(`select "CustomerId" from "CustomerLinks" where "LoginSub" = '${fixtureLogin('customer-b').userId}'`);
  expect(dispute.CustomerId, "the dispute belongs to B's customer").toBe(owner.CustomerId);
  const asOwner = await contextB.request.get(`/api/v1/disputes/${dispute.Id}`);
  expect(asOwner.status(), 'B can read it').toBe(200);

  const before = counts(dispute.Id);
  const page = await contextA.newPage();
  const browserAnswer = page.waitForResponse((response) => new URL(response.url()).pathname === `/api/v1/disputes/${dispute.Id}`);
  await page.goto(`/disputes/${dispute.Id}`);
  expect((await browserAnswer).status()).toBe(404);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText("We can't find that dispute");
  expect(await page.locator('body').innerText()).not.toContain('Spotify');
  await expectAccessibleAtBothWidths(page, 'Not-found screen');
  await page.close();

  await pause(150);
  const other = await contextA.request.get(`/api/v1/disputes/${dispute.Id}`);
  await pause(150);
  const unknown = await contextA.request.get(`/api/v1/disputes/${randomUUID()}`);
  expect(other.status()).toBe(404);
  expect(unknown.status()).toBe(404);
  expect(other.headers()['content-type']).toBe(unknown.headers()['content-type']);
  const otherBody = await other.json();
  expect(expectProblemShape(otherBody, 'not-found', 404)).toEqual([]);
  expect(withoutTraceId(otherBody)).toEqual(withoutTraceId(await unknown.json()));
  expect(JSON.stringify(otherBody)).not.toContain(dispute.Id);

  expect(counts(dispute.Id), 'the reads wrote nothing: same Version, stored events, outbox rows and audit rows').toEqual(before);
  expect(before.auditForDispute).toBe(0);
});
