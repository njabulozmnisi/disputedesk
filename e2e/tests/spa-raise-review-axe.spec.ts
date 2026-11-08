import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { axeTags, signInContext } from './support';

for (const [width, scheme] of [[360, 'light'], [360, 'dark'], [1280, 'light'], [1280, 'dark'], [320, 'light']] as const) {
  test(`raise and review axe, ${width} ${scheme}`, async ({ browser }) => {
    const context = await signInContext(browser, 'customer-a');
    const page = await context.newPage();
    await page.setViewportSize({ width, height: 900 });
    await page.emulateMedia({ colorScheme: scheme });
    const check = async (label: string) => {
      const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
      expect(result.violations.map((v) => `${label}: ${v.id} ${v.nodes.length}`), label).toEqual([]);
    };
    await page.goto('/transactions');
    await page.locator('a[href$="/dispute"]').first().click();
    await page.getByRole('radio', { name: /Unrecognised/ }).click({ force: true });
    await check('raise');
    await page.getByRole('radio', { name: /Wrong amount/ }).click({ force: true });
    await page.getByLabel(/Expected amount/i).fill('5000000.00');
    await page.getByRole('button', { name: /Continue/ }).click();
    await check('raise error');
    await page.getByLabel(/Expected amount/i).fill('0.50');
    await check('wrong amount difference');
    await page.getByLabel(/Expected amount/i).press('Enter');
    await page.getByRole('heading', { name: 'Review your dispute' }).waitFor();
    await check('review');
    await page.route('**/api/v1/disputes', (route) =>
      route.request().method() === 'POST'
        ? route.fulfill({
            status: 503,
            contentType: 'application/problem+json',
            body: JSON.stringify({ type: 'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/core-banking-unavailable.md', title: 'x', status: 503, traceId: '00000000000000000000000000000001' }),
          })
        : route.continue(),
    );
    await page.getByRole('button', { name: /Submit dispute/ }).click();
    await expect(page.getByRole('alert')).toHaveCount(1, { timeout: 15000 });
    await check('review 503');
    await context.close();
  });
}
