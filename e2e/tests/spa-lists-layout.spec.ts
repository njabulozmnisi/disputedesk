import { expect, test, type BrowserContext, type Locator, type Page } from '@playwright/test';
import { signInContext } from './support';

// ui-lists: the transactions and disputes lists lay out in columns from 768px. Measured with bounding boxes on each side of
// the bound (767, 768, 1280) and at 320, as customer A, read-only (nothing is raised here).

const disputeLink = /^Dispute (?!status: )/;
const badgeLink = /^Dispute status: /;
const tapTarget = 44;

let customerA: BrowserContext;

test.beforeAll(async ({ browser }) => {
  customerA = await signInContext(browser, 'customer-a');
});
test.afterAll(async () => customerA.close().catch(() => undefined));

async function boxes(locator: Locator) {
  const count = await locator.count();
  const result = [];
  for (let index = 0; index < count; index += 1) result.push((await locator.nth(index).boundingBox())!);
  return result;
}

const horizontalOverflow = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);

// The column header is the panel's aria-hidden row that holds the labels; the loading skeleton has its own, so wait for rows.
const columnHeader = (page: Page) => page.locator('[aria-hidden="true"]').filter({ hasText: /^\s*Transaction\s*Amount\s*Dispute\s*$/ });

test.describe('transactions', () => {
  for (const width of [767, 768, 1280]) {
    test(`${width}px: header, aligned columns, 44px targets`, async () => {
      const page = await customerA.newPage();
      await page.setViewportSize({ width, height: 900 });
      await page.goto('/transactions');
      const rows = page.locator('li');
      await expect(page.getByRole('link', { name: disputeLink }).first()).toBeVisible();
      const columns = width >= 768;

      if (columns) await expect(columnHeader(page)).toBeVisible();
      else await expect(columnHeader(page)).toBeHidden();
      await expect(columnHeader(page)).toHaveAttribute('aria-hidden', 'true');

      const amounts = await boxes(rows.locator('span.tabular-nums'));
      const actions = await boxes(rows.getByRole('link', { name: /^Dispute / }));
      expect(amounts.length).toBeGreaterThan(5);
      expect(actions.length).toBeGreaterThan(0);
      const rightEdges = amounts.map((box) => Math.round(box.x + box.width));
      expect(Math.max(...rightEdges) - Math.min(...rightEdges), 'every amount ends on one right edge').toBeLessThanOrEqual(1);

      for (const link of await rows.getByRole('link', { name: /^Dispute / }).all()) {
        const box = (await link.boundingBox())!;
        expect(box.height, `${await link.getAttribute('href')} is at least 44px tall`).toBeGreaterThanOrEqual(tapTarget - 0.5);
      }

      if (columns) {
        const leftEdges = actions.map((box) => Math.round(box.x));
        expect(Math.max(...leftEdges) - Math.min(...leftEdges), 'every action starts on one left edge').toBeLessThanOrEqual(1);
        const header = (await columnHeader(page).boundingBox())!;
        const firstRow = (await rows.first().boundingBox())!;
        expect(header.y + header.height, 'the header sits above the first row').toBeLessThanOrEqual(firstRow.y + 1);
      } else {
        // Below 768px the Dispute link is under the amount, not in a column of its own.
        const firstLink = await rows.filter({ has: page.getByRole('link', { name: disputeLink }) }).first();
        const amount = (await firstLink.locator('span.tabular-nums').boundingBox())!;
        const link = (await firstLink.getByRole('link', { name: disputeLink }).boundingBox())!;
        expect(link.y).toBeGreaterThanOrEqual(amount.y + amount.height - 12);
      }
      for (const badge of await rows.getByRole('link', { name: badgeLink }).all()) {
        expect((await badge.boundingBox())!.height).toBeGreaterThanOrEqual(tapTarget - 0.5);
      }
      await page.close();
    });
  }

  test('320px: no horizontal scroll', async () => {
    const page = await customerA.newPage();
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto('/transactions');
    await expect(page.getByRole('link', { name: disputeLink }).first()).toBeVisible();
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0);
    await page.close();
  });
});

test.describe('disputes', () => {
  for (const width of [767, 768, 1280]) {
    test(`${width}px: one link per row, chevron from 768px, badge placement`, async () => {
      const page = await customerA.newPage();
      await page.setViewportSize({ width, height: 900 });
      await page.goto('/disputes');
      const rows = page.locator('li').filter({ has: page.getByRole('link') });
      await expect(rows.first()).toBeVisible();
      const count = await rows.count();
      expect(count).toBeGreaterThan(2);
      for (let index = 0; index < count; index += 1) expect(await rows.nth(index).getByRole('link').count(), 'one link, one tab stop').toBe(1);

      const chevrons = rows.locator('svg.lucide-chevron-right');
      expect(await chevrons.count()).toBe(count);
      if (width >= 768) await expect(chevrons.first()).toBeVisible();
      else await expect(chevrons.first()).toBeHidden();

      const amounts = await boxes(rows.locator('p.tabular-nums'));
      const rightEdges = amounts.map((box) => Math.round(box.x + box.width));
      expect(Math.max(...rightEdges) - Math.min(...rightEdges), 'every amount ends on one right edge').toBeLessThanOrEqual(1);

      const first = rows.first();
      const text = (await first.locator('div').first().boundingBox())!;
      const badge = (await first.locator('div').nth(1).boundingBox())!;
      if (width >= 768) {
        expect(badge.x, 'the badge is in the action column, right of the text').toBeGreaterThan(text.x + text.width - 1);
        const lefts = (await boxes(rows.locator('a > div:nth-of-type(2)'))).map((box) => Math.round(box.x));
        expect(lefts.length).toBe(count);
        expect(Math.max(...lefts) - Math.min(...lefts), 'every badge starts on one left edge').toBeLessThanOrEqual(1);
      } else {
        expect(badge.y, 'the badge is on its own line under the text').toBeGreaterThanOrEqual(text.y + text.height - 1);
      }
      await page.close();
    });
  }

  test('320px: no horizontal scroll', async () => {
    const page = await customerA.newPage();
    await page.setViewportSize({ width: 320, height: 800 });
    await page.goto('/disputes');
    await expect(page.locator('li').first()).toBeVisible();
    expect(await horizontalOverflow(page)).toBeLessThanOrEqual(0);
    await page.close();
  });
});

// Fix round 1 (QA Q1): at 200% text on a phone every status badge stays inside its panel, which clips what overflows it.
for (const path of ['/transactions', '/disputes']) {
  test(`${path} 360px, root font 200%: every status badge is inside its panel`, async () => {
    const page = await customerA.newPage();
    await page.setViewportSize({ width: 360, height: 800 });
    await page.goto(path);
    // CSSOM, not a style tag: the page's CSP is style-src 'self', which blocks an injected <style>.
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%'; });
    // The badge is the grandparent of its sr-only "Dispute status:" prefix (a link on /transactions, inside the row link on /disputes).
    const badges = page.locator('span.sr-only', { hasText: 'Dispute status:' }).locator('xpath=../..');
    await expect(badges.first()).toBeVisible();
    expect(await badges.count()).toBeGreaterThan(2);
    for (const badge of await badges.all()) {
      const panel = badge.locator('xpath=ancestor::*[contains(concat(" ", normalize-space(@class), " "), " overflow-hidden ")][1]');
      const panelBox = (await panel.boundingBox())!;
      const badgeBox = (await badge.boundingBox())!;
      const label = (await badge.innerText()).trim();
      expect(badgeBox.x, label + ' starts inside the panel').toBeGreaterThanOrEqual(panelBox.x - 0.5);
      expect(badgeBox.x + badgeBox.width, label + ' ends inside the panel').toBeLessThanOrEqual(panelBox.x + panelBox.width + 0.5);
    }
    await page.close();
  });
}
