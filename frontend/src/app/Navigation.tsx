import { Link } from '@tanstack/react-router';

import { cn } from '@/lib/utils';

// Two destinations only (EXPERIENCE.md, Information Architecture). From 768px they sit in the header; below that they
// float at the foot of the screen. `Link` sets `aria-current="page"` on the active one.
const linkBase =
  'items-center rounded-full font-strong text-muted-foreground no-underline aria-[current=page]:bg-primary aria-[current=page]:text-primary-foreground';

const topLinkClass = cn(linkBase, 'inline-flex min-h-10 px-4 py-2 hover:text-foreground');
const tabLinkClass = cn(linkBase, 'flex min-h-12 flex-1 justify-center gap-2 px-3 text-label');

export function TopNav() {
  return (
    <nav aria-label="Main" className="hidden gap-1 rounded-full bg-muted p-1 md:flex">
      <Link to="/transactions" className={topLinkClass}>
        Transactions
      </Link>
      <Link to="/disputes" className={topLinkClass}>
        Disputes
      </Link>
    </nav>
  );
}

export function TabBar() {
  return (
    <nav
      aria-label="Main (phone)"
      className="fixed inset-x-4 bottom-4 z-10 flex gap-1 rounded-full border border-border bg-card p-1 shadow-overlay md:hidden"
    >
      <Link to="/transactions" className={tabLinkClass}>
        <span aria-hidden="true">☰</span>
        Transactions
      </Link>
      <Link to="/disputes" className={tabLinkClass}>
        <span aria-hidden="true">⚑</span>
        Disputes
      </Link>
    </nav>
  );
}
