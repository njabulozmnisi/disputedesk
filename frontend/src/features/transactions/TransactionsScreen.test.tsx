import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import type { TransactionResponse } from '@/api/generated/model';
import { server } from '@/test/server';
import {
  failWith,
  makeRow,
  renderApp,
  serveDisputes,
  serveTransactions,
  signInAs,
} from '@/test/support';

beforeEach(() => {
  signInAs('linked');
  serveDisputes();
});

/** Serves two pages by cursor and records the `cursor` query parameter of each request. */
function serveTwoPages(first: TransactionResponse[], second: TransactionResponse[]) {
  const cursors: (string | null)[] = [];
  server.use(
    http.get('*/api/v1/transactions', ({ request }) => {
      const cursor = new URL(request.url).searchParams.get('cursor');
      cursors.push(cursor);
      return cursor === null
        ? HttpResponse.json({ items: first, nextCursor: 'cursor-for-page-2' })
        : HttpResponse.json({ items: second, nextCursor: null });
    }),
  );
  return cursors;
}

// Criterion 5: the list.
describe('transaction rows', () => {
  it('shows the description, the masked card, the amount and the day in South African time', async () => {
    serveTransactions([
      makeRow({
        transactionId: 'TXN-A',
        description: 'Karoo Fuel Stop, Pinetown',
        // 23:30 UTC on the 26th is 01:30 on the 27th in Johannesburg.
        transactionDate: '2026-10-26T23:30:00Z',
        amount: { amount: '650.00', currency: 'ZAR' },
        maskedCard: '492345******3456',
      }),
    ]);
    renderApp({ path: '/transactions' });

    const row = (await screen.findByText('Karoo Fuel Stop, Pinetown')).closest('li');
    expect(row).not.toBeNull();
    expect(screen.getByRole('heading', { level: 2, name: 'Tue 27 Oct' })).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText('Card 492345•• •••• 3456')).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText('card ending 3456')).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText('R650.00')).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText('R650.00 out')).toBeInTheDocument();
  });

  it('shows an account row by its last four digits, and a credit with a plus and "in"', async () => {
    serveTransactions([
      makeRow({
        description: 'Salary: Ridgeview Primary',
        amount: { amount: '18450.00', currency: 'ZAR' },
        direction: 'credit',
        kind: 'transfer',
        maskedCard: null,
        accountLast4: '4821',
        disputable: false,
        notDisputableReason: 'notEligible',
      }),
    ]);
    renderApp({ path: '/transactions' });

    const row = (await screen.findByText('Salary: Ridgeview Primary')).closest('li') as HTMLElement;
    expect(within(row).getByText('Account •••• 4821')).toBeInTheDocument();
    expect(within(row).getByText('account ending 4821')).toBeInTheDocument();
    // Testing Library would fold U+202F into a plain space, so the text is matched as it is.
    const exactly = { normalizer: (text: string) => text };
    expect(within(row).getByText('+R18\u202F450.00', exactly)).toBeInTheDocument();
    expect(within(row).getByText('R18\u202F450.00 in', exactly)).toBeInTheDocument();
  });

  it('has no accessibility violations with disputable and not disputable rows', async () => {
    serveTransactions([
      makeRow({ description: 'Can dispute' }),
      makeRow({
        transactionId: 'TXN-NO',
        description: 'Cannot dispute',
        disputable: false,
        notDisputableReason: 'outsideWindow',
      }),
    ]);
    renderApp({ path: '/transactions' });
    await screen.findByText('Can dispute');

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('groups rows made on the same day under one heading, newest first', async () => {
    serveTransactions([
      makeRow({
        transactionId: 'TXN-1',
        description: 'Later',
        transactionDate: '2026-10-26T15:00:00Z',
      }),
      makeRow({
        transactionId: 'TXN-2',
        description: 'Earlier',
        transactionDate: '2026-10-26T08:00:00Z',
      }),
      makeRow({
        transactionId: 'TXN-3',
        description: 'Day before',
        transactionDate: '2026-10-25T10:00:00Z',
      }),
    ]);
    renderApp({ path: '/transactions' });

    await screen.findByText('Later');
    const headings = screen
      .getAllByRole('heading', { level: 2 })
      .map((heading) => heading.textContent);
    expect(headings).toEqual(['Mon 26 Oct', 'Sun 25 Oct']);
    const descriptions = screen
      .getAllByRole('listitem')
      .map((item) => item.querySelector('p')?.textContent);
    expect(descriptions).toEqual(['Later', 'Earlier', 'Day before']);
  });
});

describe('Load more', () => {
  const firstPage = [
    makeRow({
      transactionId: 'TXN-1',
      description: 'First page row 1',
      transactionDate: '2026-10-26T10:00:00Z',
    }),
    makeRow({
      transactionId: 'TXN-2',
      description: 'First page row 2',
      transactionDate: '2026-10-25T10:00:00Z',
    }),
  ];
  const secondPage = [
    makeRow({
      transactionId: 'TXN-3',
      description: 'Second page row 1',
      transactionDate: '2026-10-24T10:00:00Z',
    }),
    makeRow({
      transactionId: 'TXN-4',
      description: 'Second page row 2',
      transactionDate: '2026-10-23T10:00:00Z',
    }),
  ];

  it('asks for the next page with nextCursor, adds the rows, and goes away when nextCursor is null', async () => {
    const cursors = serveTwoPages(firstPage, secondPage);
    const { user } = renderApp({ path: '/transactions' });

    await screen.findByText('First page row 1');
    expect(cursors).toEqual([null]);

    await user.click(screen.getByRole('button', { name: 'Load more' }));

    expect(await screen.findByText('Second page row 2')).toBeInTheDocument();
    expect(cursors).toEqual([null, 'cursor-for-page-2']);
    expect(screen.getByText('First page row 1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('moves focus to the first new row and announces the new rows politely', async () => {
    serveTwoPages(firstPage, secondPage);
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByText('First page row 1');

    await user.click(screen.getByRole('button', { name: 'Load more' }));

    await screen.findByText('Second page row 1');
    const firstNewRow = screen.getByText('Second page row 1').closest('li');
    await waitFor(() => {
      expect(firstNewRow).toHaveFocus();
    });
    expect(firstNewRow).toHaveAttribute('tabindex', '-1');
    const liveRegion = screen.getByRole('status');
    expect(liveRegion).toHaveAttribute('aria-live', 'polite');
    expect(liveRegion).toHaveTextContent('2 more transactions loaded');
  });

  it('says "1 more transaction loaded" for a single new row', async () => {
    serveTwoPages(firstPage, secondPage.slice(0, 1));
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByText('First page row 1');

    await user.click(screen.getByRole('button', { name: 'Load more' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('1 more transaction loaded');
    });
  });

  it('shows Loading… while the page is fetched, stays focusable, and cannot be pressed twice', async () => {
    let requests = 0;
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('*/api/v1/transactions', async ({ request }) => {
        requests += 1;
        if (new URL(request.url).searchParams.get('cursor') === null) {
          return HttpResponse.json({ items: firstPage, nextCursor: 'cursor-for-page-2' });
        }
        await held;
        return HttpResponse.json({ items: secondPage, nextCursor: null });
      }),
    );
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByText('First page row 1');

    const button = screen.getByRole('button', { name: 'Load more' });
    await user.click(button);
    const loading = await screen.findByRole('button', { name: 'Loading…' });
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).not.toBeDisabled();
    await user.click(loading);
    expect(requests).toBe(2);

    release();
    expect(await screen.findByText('Second page row 2')).toBeInTheDocument();
    expect(requests).toBe(2);
  });

  it('keeps the rows and shows the error with Try again when the next page fails', async () => {
    let calls = 0;
    server.use(
      http.get('*/api/v1/transactions', ({ request }) => {
        calls += 1;
        const cursor = new URL(request.url).searchParams.get('cursor');
        if (cursor === null)
          return HttpResponse.json({ items: firstPage, nextCursor: 'cursor-for-page-2' });
        return calls < 4
          ? HttpResponse.json(
              { title: 'broken' },
              { status: 500, headers: { 'content-type': 'application/problem+json' } },
            )
          : HttpResponse.json({ items: secondPage, nextCursor: null });
      }),
    );
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByText('First page row 1');

    await user.click(screen.getByRole('button', { name: 'Load more' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(screen.getByText('First page row 1')).toBeInTheDocument();

    calls = 3;
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText('Second page row 2')).toBeInTheDocument();
  });

  it('ends at the session-ended screen when Load more gets a 401', async () => {
    server.use(
      http.get('*/api/v1/transactions', ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor');
        if (cursor === null)
          return HttpResponse.json({ items: firstPage, nextCursor: 'cursor-for-page-2' });
        return HttpResponse.json(
          { title: 'unauthorized' },
          { status: 401, headers: { 'content-type': 'application/problem+json' } },
        );
      }),
    );
    const { user, router } = renderApp({ path: '/transactions' });
    await screen.findByText('First page row 1');

    await user.click(screen.getByRole('button', { name: 'Load more' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    expect(
      await screen.findByRole('heading', { level: 1, name: "You've been signed out" }),
    ).toBeInTheDocument();
  });
});

// Criterion 6: disputability on each row.
describe('disputability', () => {
  it('shows the Dispute control only on a disputable row', async () => {
    serveTransactions([
      makeRow({ transactionId: 'TXN-OK', description: 'Can dispute' }),
      makeRow({
        transactionId: 'TXN-NO',
        description: 'Cannot dispute',
        disputable: false,
        notDisputableReason: 'outsideWindow',
      }),
    ]);
    renderApp({ path: '/transactions' });

    const disputable = (await screen.findByText('Can dispute')).closest('li') as HTMLElement;
    const notDisputable = screen.getByText('Cannot dispute').closest('li') as HTMLElement;
    const dispute = within(disputable).getByRole('link', { name: 'Dispute this transaction' });
    expect(dispute).toHaveAttribute('href', '/transactions/TXN-OK/dispute');
    expect(within(notDisputable).queryByRole('link')).not.toBeInTheDocument();
    expect(within(disputable).queryByText(/disputable|Older than|Pending/)).not.toBeInTheDocument();
  });

  it.each([
    [
      'older than 90 days',
      { notDisputableReason: 'outsideWindow' as const },
      'Older than 90 days. Contact the bank about this transaction.',
    ],
    [
      'pending',
      { state: 'pending' as const, notDisputableReason: 'notEligible' as const },
      'Pending. You can dispute it once it has settled.',
    ],
    [
      'a credit',
      {
        direction: 'credit' as const,
        kind: 'transfer' as const,
        notDisputableReason: 'notEligible' as const,
      },
      'Money in. Not disputable.',
    ],
    [
      'a transfer between own accounts',
      { kind: 'ownAccountTransfer' as const, notDisputableReason: 'notEligible' as const },
      'Transfer between your accounts. Not disputable.',
    ],
    [
      'a settled debit that is not a card payment',
      { kind: 'transfer' as const, notDisputableReason: 'notEligible' as const },
      'Not a card payment. Contact the bank about this transaction.',
    ],
  ])('says why a row is not disputable when it is %s', async (_name, overrides, hint) => {
    serveTransactions([makeRow({ description: 'Why not', disputable: false, ...overrides })]);
    renderApp({ path: '/transactions' });

    const row = (await screen.findByText('Why not')).closest('li') as HTMLElement;
    expect(within(row).getByText(hint)).toBeInTheDocument();
    expect(within(row).queryByRole('link')).not.toBeInTheDocument();
  });
});

const disputeId = '3f2b8c1e-7d4a-4e5b-9a60-1c2d3e4f5a6b';
const statusCases = [
  ['submitted', 'Submitted'],
  ['underReview', 'Under review'],
  ['underReviewPrioritised', 'Under review (prioritised)'],
  ['resolved', 'Resolved'],
  ['withdrawn', 'Withdrawn'],
] as const;

/** A row the way the API sends it once a dispute exists: not disputable, `alreadyDisputed`. */
function disputedRow(overrides: Partial<TransactionResponse>) {
  return makeRow({
    description: 'Already raised',
    disputable: false,
    notDisputableReason: 'alreadyDisputed',
    disputeId,
    customerStatus: 'submitted',
    ...overrides,
  });
}

// Story 3.10, criterion 2: a disputed row shows its status badge as a link.
describe('dispute status badge on a row', () => {
  it.each(statusCases)(
    'shows %s as the only link, with no Dispute control and no hint',
    async (customerStatus, label) => {
      serveTransactions([disputedRow({ customerStatus })]);
      renderApp({ path: '/transactions' });

      const row = (await screen.findByText('Already raised')).closest('li') as HTMLElement;
      const links = within(row).getAllByRole('link');
      expect(links).toHaveLength(1);
      expect(links[0]).toHaveAccessibleName(`Dispute status: ${label}`);
      expect(links[0]).toHaveAttribute('href', `/disputes/${disputeId}`);
      expect(within(row).queryByText(/Dispute this transaction/)).not.toBeInTheDocument();
      expect(
        within(row).queryByText(/Not a card payment|Contact the bank|Not disputable/),
      ).toBeNull();
      expect(within(row).queryByText('ⓘ', { exact: false })).not.toBeInTheDocument();
    },
  );

  it('hides the icon from the accessible name and keeps the dispute id out of the text', async () => {
    serveTransactions([disputedRow({ customerStatus: 'underReviewPrioritised' })]);
    renderApp({ path: '/transactions' });

    const link = await screen.findByRole('link', {
      name: 'Dispute status: Under review (prioritised)',
    });
    expect(link.querySelector('[aria-hidden="true"]')).toHaveTextContent('▲');
    expect(screen.queryByText(new RegExp(disputeId))).not.toBeInTheDocument();
    expect(link).not.toHaveAccessibleName(expect.stringContaining(disputeId));
  });

  it('encodes the dispute id in the href', async () => {
    serveTransactions([disputedRow({ disputeId: '../x?y#z' })]);
    renderApp({ path: '/transactions' });

    expect(await screen.findByRole('link', { name: 'Dispute status: Submitted' })).toHaveAttribute(
      'href',
      '/disputes/..%2Fx%3Fy%23z',
    );
  });

  // Criterion 3: defence in depth, the server's mapping is the control.
  it('shows no badge, no Dispute control and no hint for an unknown status, a half-set pair or a missing or empty id', async () => {
    const idKeyOmitted = disputedRow({
      transactionId: 'TXN-O',
      description: 'Id omitted',
      customerStatus: 'resolved',
    });
    Reflect.deleteProperty(idKeyOmitted, 'disputeId');
    serveTransactions([
      disputedRow({
        transactionId: 'TXN-U',
        description: 'Unknown status',
        customerStatus: 'escalated' as TransactionResponse['customerStatus'],
      }),
      disputedRow({ transactionId: 'TXN-H', description: 'Half set', customerStatus: null }),
      disputedRow({
        transactionId: 'TXN-S',
        description: 'Status only',
        disputeId: null,
        customerStatus: 'resolved',
      }),
      disputedRow({
        transactionId: 'TXN-E',
        description: 'Empty id',
        disputeId: '',
        customerStatus: 'resolved',
      }),
      idKeyOmitted,
    ]);
    renderApp({ path: '/transactions' });

    await screen.findByText('Unknown status');
    expect(screen.getAllByRole('listitem')).toHaveLength(5);
    expect(within(screen.getByRole('list')).queryByRole('link')).not.toBeInTheDocument();
    expect(screen.queryByText(/escalated/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/Not a card payment|Contact the bank|Not disputable/)).toBeNull();
  });

  // Criterion 4: rows without a dispute are unchanged.
  it('shows no badge on a disputable row, which keeps its Dispute link', async () => {
    serveTransactions([makeRow({ description: 'Open row' })]);
    renderApp({ path: '/transactions' });

    const row = (await screen.findByText('Open row')).closest('li') as HTMLElement;
    expect(within(row).getByRole('link', { name: 'Dispute this transaction' })).toBeInTheDocument();
    expect(within(row).queryByRole('link', { name: /Dispute status/ })).not.toBeInTheDocument();
  });

  // Criterion 5: the badge reads only the list row.
  it('makes no request beyond the session and the transactions list', async () => {
    const paths: string[] = [];
    server.events.on('request:start', ({ request }) => {
      paths.push(new URL(request.url).pathname);
    });
    serveTransactions(
      statusCases.map(([customerStatus], index) =>
        disputedRow({
          transactionId: `TXN-${String(index)}`,
          description: `Row ${String(index)}`,
          customerStatus,
        }),
      ),
    );
    renderApp({ path: '/transactions' });
    await screen.findByRole('link', { name: 'Dispute status: Withdrawn' });

    expect(new Set(paths)).toEqual(new Set(['/api/v1/bff/session', '/api/v1/transactions']));
  });

  // Mockup a.badge:hover: the label underlines on hover, and the link is full-radius so its focus ring matches.
  it('rounds the link and underlines the label on hover', async () => {
    serveTransactions([disputedRow({})]);
    renderApp({ path: '/transactions' });

    const link = await screen.findByRole('link', { name: 'Dispute status: Submitted' });
    expect(link).toHaveClass('rounded-full');
    expect(link).toHaveClass('group');
    expect(within(link).getByText('Submitted')).toHaveClass('group-hover:underline');
  });
});

// Criterion 7: loading, empty and error.
describe('loading, empty and error states', () => {
  it('shows a skeleton with aria-busy on the list while the first page loads', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('*/api/v1/transactions', async () => {
        await held;
        return HttpResponse.json({
          items: [makeRow({ description: 'Arrived' })],
          nextCursor: null,
        });
      }),
    );
    renderApp({ path: '/transactions' });

    const list = await screen.findByRole('list', { name: 'Loading transactions' });
    expect(list).toHaveAttribute('aria-busy', 'true');
    expect(within(list).getAllByRole('listitem')).toHaveLength(5);
    expect(screen.getByRole('heading', { level: 1, name: 'Transactions' })).toBeInTheDocument();

    release();
    expect(await screen.findByText('Arrived')).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Loading transactions' })).not.toBeInTheDocument();
  });

  it('has no accessibility violations while loading', async () => {
    server.use(http.get('*/api/v1/transactions', () => new Promise(() => undefined)));
    renderApp({ path: '/transactions' });
    await screen.findByRole('list', { name: 'Loading transactions' });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('has no accessibility violations in the empty state', async () => {
    serveTransactions([]);
    renderApp({ path: '/transactions' });
    await screen.findByRole('heading', { level: 2, name: 'No transactions in the last 90 days.' });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('has no accessibility violations in the error state', async () => {
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);
    renderApp({ path: '/transactions' });
    await screen.findByRole('alert');

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('shows the empty state, with Disputes as a link and no button', async () => {
    serveTransactions([]);
    const { router } = renderApp({ path: '/transactions' });

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: 'No transactions in the last 90 days.',
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /Card and account transactions appear here once they reach your account\. Disputes you raised earlier are still in/,
      ),
    ).toBeInTheDocument();
    const link = within(screen.getByRole('main')).getByRole('link', { name: 'Disputes' });
    expect(link).toHaveAttribute('href', '/disputes');
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();

    // The router takes the click over (`preventDefault`), so the browser makes no document request.
    expect(fireEvent.click(link)).toBe(false);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/disputes');
    });
  });

  it('shows the core-banking notice with the reference, Try again and View disputes for a 503', async () => {
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);
    const { router } = renderApp({ path: '/transactions' });

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent("We can't load your transactions right now.");
    expect(notice).toHaveTextContent('Your disputes are still available.');
    expect(notice).toHaveTextContent('trace-1');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    const link = screen.getByRole('link', { name: 'View disputes' });
    expect(link).toHaveAttribute('href', '/disputes');

    expect(fireEvent.click(link)).toBe(false);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/disputes');
    });
  });

  it('shows the generic notice, without View disputes, for any other failure', async () => {
    server.use(
      http.get('*/api/v1/transactions', () =>
        HttpResponse.json(
          { title: 'x' },
          { status: 500, headers: { 'content-type': 'application/problem+json' } },
        ),
      ),
    );
    renderApp({ path: '/transactions' });

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(screen.queryByRole('link', { name: 'View disputes' })).not.toBeInTheDocument();
  });

  it("calls the query's refetch from Try again and shows the rows when it works", async () => {
    let requests = 0;
    server.use(
      http.get('*/api/v1/transactions', () => {
        requests += 1;
        return requests <= 2
          ? HttpResponse.json(
              { title: 'down' },
              { status: 503, headers: { 'content-type': 'application/problem+json' } },
            )
          : HttpResponse.json({
              items: [makeRow({ description: 'Back again' })],
              nextCursor: null,
            });
      }),
    );
    const { user } = renderApp({ path: '/transactions' });

    await user.click(await screen.findByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Back again')).toBeInTheDocument();
    expect(requests).toBe(3);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('copies the reference and confirms with Copied', async () => {
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);
    const { user } = renderApp({ path: '/transactions' });

    await user.click(await screen.findByRole('button', { name: 'Copy' }));

    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
    expect(await navigator.clipboard.readText()).toBe('trace-1');
  });
});

// Criterion 8: every control is reachable by Tab, in reading order.
describe('keyboard', () => {
  it('reaches every control by Tab in reading order', async () => {
    serveTwoPages(
      [
        makeRow({ transactionId: 'TXN-1', description: 'One' }),
        makeRow({
          transactionId: 'TXN-2',
          description: 'Two',
          disputable: false,
          notDisputableReason: 'outsideWindow',
        }),
        makeRow({ transactionId: 'TXN-3', description: 'Three' }),
      ],
      [],
    );
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByText('One');

    const order: string[] = [];
    for (let index = 0; index < 10; index += 1) {
      await user.tab();
      const focused = document.activeElement;
      order.push(`${focused?.tagName.toLowerCase() ?? '?'}:${focused?.textContent.trim() ?? ''}`);
    }

    expect(order).toEqual([
      'a:Skip to main content',
      'a:DisputeDesk',
      'a:Transactions',
      'a:Disputes',
      'button:Sign out',
      'a:Dispute this transaction',
      'a:Dispute this transaction',
      'button:Load more',
      'a:☰Transactions',
      'a:⚑Disputes',
    ]);
  });
});
