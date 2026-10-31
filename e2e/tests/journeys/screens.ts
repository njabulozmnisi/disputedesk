import AxeBuilder from '@axe-core/playwright';
import { expect, type Page } from '@playwright/test';

// Copied from the regression specs, which each declare their own (story 7.2 pre-build review, finding 5).
export const axeTags = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** Axe at 1280px and at 320px for the screen state the page is showing now, plus the 320px reflow check (WCAG 2.2 SC 1.4.10). Leaves the page at 1280px. */
export async function expectAccessibleAtBothWidths(page: Page, state: string) {
  for (const width of [320, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    const result = await new AxeBuilder({ page }).withTags(axeTags).analyze();
    expect(result.violations.map((violation) => `${state} @${width}px ${violation.id}: ${violation.nodes.map((node) => node.target.join(' ')).join('; ')}`)).toEqual([]);
    if (width === 320) {
      const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
      expect(scrollWidth, `${state}: no horizontal scroll at 320px`).toBeLessThanOrEqual(320);
    }
  }
}
