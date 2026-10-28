import { appSourceFiles } from '@/test/sourceFiles';

// Criterion 8: colours and type come only from src/theme/tokens.css, exposed to Tailwind through @theme.
describe('the theme file', () => {
  const outsideTheme = appSourceFiles(['.ts', '.tsx', '.css']).filter(
    ({ path }) => !path.startsWith('theme'),
  );

  it.each([
    ['a hex colour', /#[0-9a-fA-F]{3,8}\b/],
    ['an rgb, hsl or oklch colour', /\b(rgba?|hsla?|oklch|oklab|lab|lch|hwb)\(/],
    [
      'a Tailwind palette colour',
      /\b(bg|text|border|ring|fill|stroke|from|to|via|outline|divide|shadow|decoration|accent|caret)-(red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose|slate|gray|zinc|neutral|stone|black|white)\b/,
    ],
    ['an arbitrary font size or family', /\b(text|font)-\[/],
    ['an arbitrary letter spacing or line height', /\b(tracking|leading)-\[/],
    ['a default letter spacing', /\btracking-(tighter|tight|normal|wide|wider|widest)\b/],
    // Line heights come with the text-* size tokens (--text-display--line-height and the like).
    ['a leading utility', /\bleading-/],
  ])('has no %s outside tokens.css', (_name, pattern) => {
    const offenders = outsideTheme.filter(({ text }) => pattern.test(text)).map(({ path }) => path);

    expect(offenders).toEqual([]);
  });

  it('declares the tokens and maps them to Tailwind with @theme inline', () => {
    const tokens = appSourceFiles(['.css']).find(({ path }) => path === 'theme/tokens.css');

    expect(tokens?.text).toContain('--primary:');
    expect(tokens?.text).toContain('@media (prefers-color-scheme: dark)');
    expect(tokens?.text).toContain('@theme inline');
    expect(tokens?.text).toContain('--color-primary: var(--primary)');
  });

  // DESIGN.md, Typography: every size has its own line height, so no text falls back to preflight's 1.5.
  it('gives every --text-* size its --text-*--line-height', () => {
    const css =
      appSourceFiles(['.css']).find(({ path }) => path === 'theme/tokens.css')?.text ?? '';
    const sizes = new Set(
      [...css.matchAll(/^\s*--text-([a-z]+(?:-[a-z]+)*):/gm)].map(([, size]) => String(size)),
    );

    expect(sizes.size).toBeGreaterThan(0);
    const withoutLineHeight = [...sizes].filter(
      (size) => !new RegExp(`^\\s*--text-${size}--line-height:`, 'm').test(css),
    );
    expect(withoutLineHeight).toEqual([]);
  });

  it('loads the tokens into the app stylesheet', () => {
    const stylesheet = appSourceFiles(['.css']).find(({ path }) => path === 'index.css');

    expect(stylesheet?.text).toContain("@import './theme/tokens.css'");
  });
});
