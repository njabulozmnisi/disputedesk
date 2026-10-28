import { cn } from './utils';

describe('cn', () => {
  it('keeps both the theme font size and the colour of a button: a size is not a colour', () => {
    expect(cn('text-primary-foreground', 'text-body')).toBe('text-primary-foreground text-body');
    expect(cn('text-label', 'text-primary')).toBe('text-label text-primary');
  });

  it('still lets a later class of the same kind win', () => {
    expect(cn('text-body', 'text-label')).toBe('text-label');
    expect(cn('text-primary', 'text-foreground')).toBe('text-foreground');
    expect(cn('font-label', 'font-strong')).toBe('font-strong');
  });
});
