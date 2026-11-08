/**
 * A Lucide icon is decorative and drawn the same everywhere (Lucide, Accessibility; WCAG 2.2 SC 1.1.1): hidden from
 * assistive technology, coloured by the text beside it, Lucide's default stroke. jsdom leaves `width` and `height` at
 * Lucide's 24, so the size is the Tailwind class.
 */
export function expectDecorativeIcon(icon: Element | null | undefined, sizeClass = 'size-4') {
  expect(icon).toBeInTheDocument();
  expect(icon).toHaveAttribute('aria-hidden', 'true');
  expect(icon).toHaveAttribute('stroke', 'currentColor');
  expect(icon).toHaveAttribute('stroke-width', '2');
  expect(icon).toHaveClass(sizeClass);
}
