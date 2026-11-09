import { cn } from './utils';

describe('cn', () => {
  it('keeps both the theme font size and the colour of a button: a size is not a colour', () => {
    expect(cn('text-primary-foreground', 'text-body')).toBe('text-primary-foreground text-body');
    expect(cn('text-label', 'text-primary')).toBe('text-label text-primary');
  });

  it('reads text-heading as the navy colour, not a size, so it keeps the size beside it', () => {
    expect(cn('text-label text-muted-foreground', 'text-label font-strong text-heading')).toBe(
      'text-label font-strong text-heading',
    );
  });

  it('reads text-h2 as a size, so it replaces another size', () => {
    expect(cn('text-label', 'text-h2')).toBe('text-h2');
  });

  it('keeps text-h2 beside text-heading: a size and the navy colour', () => {
    expect(cn('text-h2', 'text-heading')).toBe('text-h2 text-heading');
  });

  it('still lets a later class of the same kind win', () => {
    expect(cn('text-body', 'text-label')).toBe('text-label');
    expect(cn('text-primary', 'text-foreground')).toBe('text-foreground');
    expect(cn('font-label', 'font-strong')).toBe('font-strong');
  });
});
