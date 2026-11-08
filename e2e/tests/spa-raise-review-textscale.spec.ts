import { expect, test, type Page } from '@playwright/test';
import { signInContext } from './support';

// WCAG 2.2 SC 1.4.4 Resize Text: with text-only scaling the review list keeps room for its values and clips nothing.
// The whole-page scroll width is not asserted here: the signed-in header scrolls at 320 and 360 px with 200% text
// (before this story; recorded in lows.md).

async function openReview(page: Page) {
  await page.goto('/transactions');
  await page.locator('a[href$="/dispute"]').first().click();
  await page.getByRole('radio', { name: /Unrecognised/ }).click({ force: true });
  // A note with a 90-character unbroken word gives the list its fifth row and its longest value.
  await page.getByLabel('Note (optional)').fill('x'.repeat(90));
  await page.getByRole('button', { name: /Continue/ }).click();
  await page.getByRole('heading', { name: 'Review your dispute' }).waitFor();
}

const reviewList = (page: Page) => page.getByRole('region', { name: 'Your dispute' }).locator('dl');

for (const width of [320, 360]) {
  for (const percent of [150, 200]) {
    test(`review list keeps its value column and clips nothing, ${width}px at ${percent}% text`, async ({ browser }) => {
      const context = await signInContext(browser, 'customer-a');
      const page = await context.newPage();
      await page.setViewportSize({ width, height: 900 });
      await openReview(page);
      await page.evaluate((value) => {
        document.documentElement.style.fontSize = value + '%';
      }, percent);

      const cells = await reviewList(page).evaluate((list) => {
        const listBox = list.getBoundingClientRect();
        return [...list.querySelectorAll('dt, dd')].map((cell) => {
          const box = cell.getBoundingClientRect();
          return {
            tag: cell.tagName,
            text: (cell.textContent ?? '').slice(0, 20),
            width: box.width,
            fontSize: parseFloat(getComputedStyle(cell).fontSize),
            clippedInside: cell.scrollWidth > cell.clientWidth,
            outsideList: box.left < listBox.left - 1 || box.right > listBox.right + 1,
          };
        });
      });

      expect(cells.length).toBeGreaterThanOrEqual(10);
      for (const cell of cells) {
        expect(cell.width, cell.tag + ' ' + cell.text + ' has room').toBeGreaterThanOrEqual(cell.fontSize * 2);
        expect(cell.clippedInside, cell.tag + ' ' + cell.text + ' scrolls inside itself').toBe(false);
        expect(cell.outsideList, cell.tag + ' ' + cell.text + ' leaves the list').toBe(false);
      }
      await context.close();
    });
  }
}

test('review list label column is 9rem at 1280px', async ({ browser }) => {
  const context = await signInContext(browser, 'customer-a');
  const page = await context.newPage();
  await page.setViewportSize({ width: 1280, height: 900 });
  await openReview(page);
  const columns = await reviewList(page).locator('> div').first().evaluate((row) => {
    const [label] = getComputedStyle(row).gridTemplateColumns.split(' ');
    return { label: parseFloat(label), rem: parseFloat(getComputedStyle(document.documentElement).fontSize) };
  });
  expect(columns.label).toBe(columns.rem * 9);
  await context.close();
});
