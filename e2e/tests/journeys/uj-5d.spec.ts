import { expect, test } from '@playwright/test';
import { expectProblemShape, fixtureLogin, queryDatabase, signInContext } from '../support';
import { expectAccessibleAtBothWidths } from './screens';

// UJ-5d (story 7.2, criteria 5 and 6): customer C's login is signed in but never linked. The API answers 403
// linking-required (the SPA never asks: its session says "unlinked", so the gate sends the browser to /link first,
// pre-build review finding 1). Needs story 6.4's /link screen. Read-only journey.

test('UJ-5d: an unlinked login is sent to linking', async ({ browser }) => {
  const linksBefore = Number(queryDatabase(`select count(*) from "CustomerLinks"`));
  const context = await signInContext(browser, 'customer-c');
  const page = await context.newPage();
  const transactionCalls: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/v1/transactions')) transactionCalls.push(request.url());
  });

  await page.goto('/transactions');
  await expect(page).toHaveURL(/\/link$/);
  await expect(page.getByRole('heading', { level: 1, name: 'Link your login' })).toBeVisible();
  expect(transactionCalls, 'the SPA sends no transactions request').toEqual([]);
  await expectAccessibleAtBothWidths(page, 'Linking details screen');

  const response = await context.request.get('/api/v1/transactions', { maxRedirects: 0 });
  expect(response.status()).toBe(403);
  expect(expectProblemShape(await response.json(), 'linking-required', 403)).toEqual([]);

  const loginSub = fixtureLogin('customer-c').userId;
  expect(Number(queryDatabase(`select count(*) from "CustomerLinks" where "LoginSub" = '${loginSub}'`))).toBe(0);
  expect(Number(queryDatabase(`select count(*) from "CustomerLinks"`))).toBe(linksBefore);
  await context.close();
});
