import { Search } from 'lucide-react';
import { render, screen } from '@testing-library/react';

import { StateSurface } from './StateSurface';

// Criterion 5: the surface renders the caller's heading, so a screen keeps its one h1 (WCAG 2.2 SC 1.3.1). The tile's
// size, colours and the surface's radius are QA's visual checks: jsdom has no layout.
describe('StateSurface', () => {
  it("renders a full-screen state under the caller's h1", () => {
    render(
      <StateSurface icon={Search} heading={<h1>We can&apos;t find that dispute</h1>}>
        <p>Check the link.</p>
      </StateSurface>,
    );

    expect(screen.getAllByRole('heading')).toHaveLength(1);
    expect(
      screen.getByRole('heading', { level: 1, name: "We can't find that dispute" }),
    ).toBeInTheDocument();
    expect(screen.getByText('Check the link.')).toBeInTheDocument();
  });

  it("renders an empty state under the caller's h2", () => {
    render(
      <StateSurface icon={Search} heading={<h2>No transactions.</h2>}>
        <p>Nothing yet.</p>
      </StateSurface>,
    );

    expect(screen.getByRole('heading', { level: 2, name: 'No transactions.' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 1 })).not.toBeInTheDocument();
  });

  it('puts a hidden 24px icon in a 48px tile before the heading', () => {
    const { container } = render(
      <StateSurface icon={Search} heading={<h1>Heading</h1>}>
        <p>Words</p>
      </StateSurface>,
    );

    const tile = container.querySelector('[aria-hidden="true"]');
    expect(tile).toHaveClass('size-12', 'bg-muted', 'text-heading');
    expect(tile?.querySelector('svg.lucide-search')).toHaveClass('size-6');
    expect(tile?.nextElementSibling).toBe(screen.getByRole('heading'));
  });

  it('is a plain wrapper without an icon: no tile and no surface classes', () => {
    const { container } = render(
      <StateSurface className="grid gap-4" heading={<h1>Heading</h1>}>
        <p>Words</p>
      </StateSurface>,
    );

    expect(container.querySelector('[aria-hidden="true"]')).not.toBeInTheDocument();
    expect(container.firstElementChild).toHaveClass('grid', 'gap-4');
    expect(container.firstElementChild).not.toHaveClass('rounded-lg');
    expect(container.firstElementChild?.firstElementChild).toBe(screen.getByRole('heading'));
  });

  it('keeps the heading element when the icon appears and goes', () => {
    const heading = <h1>Heading</h1>;
    const { rerender } = render(<StateSurface heading={heading}>Words</StateSurface>);
    const before = screen.getByRole('heading');

    rerender(
      <StateSurface icon={Search} heading={heading}>
        Words
      </StateSurface>,
    );
    expect(screen.getByRole('heading')).toBe(before);

    rerender(<StateSurface heading={heading}>Words</StateSurface>);
    expect(screen.getByRole('heading')).toBe(before);
  });
});
