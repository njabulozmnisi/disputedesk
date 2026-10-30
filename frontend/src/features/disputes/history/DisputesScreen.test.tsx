import { screen, waitFor, within } from '@testing-library/react';
import { delay, http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import type { CustomerStatus, DisputeSummaryResponse, Reason } from '@/api/generated/model';
import { server } from '@/test/server';
import {
  makeSummary,
  problemResponse,
  renderApp,
  serveDisputes,
  signInAs,
  serveTransactions,
} from '@/test/support';

beforeEach(() => {
  signInAs('linked');
  serveTransactions();
});

function summary(
  disputeId: string,
  customerStatus: CustomerStatus,
  overrides: Partial<DisputeSummaryResponse> = {},
) {
  return makeSummary({
    disputeId,
    customerStatus,
    transaction: { ...makeSummary().transaction, description: `Shop ${disputeId}` },
    ...overrides,
  });
}

/** Serves two pages by cursor and records the query string of each request. */
function serveTwoPages(first: DisputeSummaryResponse[], second: DisputeSummaryResponse[]) {
  const queries: string[] = [];
  server.use(
    http.get('*/api/v1/disputes', ({ request }) => {
      const url = new URL(request.url);
      queries.push(url.search);
      return url.searchParams.get('cursor') === null
        ? HttpResponse.json({ items: first, nextCursor: 'cursor-for-page-2' })
        : HttpResponse.json({ items: second, nextCursor: null });
    }),
  );
  return queries;
}

const sectionOf = (name: string) =>
  screen.getByRole('heading', { level: 2, name }).parentElement as HTMLElement;

// Criterion 1: history grouped Open and Closed.
describe('the history', () => {
  it('shows the title, the lede, and Open and Closed sections in API order', async () => {
    serveDisputes([
      summary('c1', 'withdrawn'),
      summary('o1', 'submitted'),
      summary('o2', 'underReview'),
      summary('c2', 'resolved', { outcome: 'upheld' }),
      summary('o3', 'underReviewPrioritised'),
    ]);
    renderApp({ path: '/disputes' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Disputes' })).toBeInTheDocument();
    expect(screen.getByText("Every dispute you've raised, newest first.")).toBeInTheDocument();
    await screen.findByText('Shop o1');
    const headings = screen
      .getAllByRole('heading', { level: 2 })
      .map((heading) => heading.textContent);
    expect(headings).toEqual(['Open', 'Closed']);
    const names = (section: HTMLElement) =>
      within(section)
        .getAllByRole('link')
        .map((link) => link.textContent);
    expect(names(sectionOf('Open'))).toEqual([
      expect.stringContaining('Shop o1'),
      expect.stringContaining('Shop o2'),
      expect.stringContaining('Shop o3'),
    ]);
    expect(names(sectionOf('Closed'))).toEqual([
      expect.stringContaining('Shop c1'),
      expect.stringContaining('Shop c2'),
    ]);
  });

  it('hides a section that has no rows', async () => {
    serveDisputes([summary('o1', 'submitted')]);
    renderApp({ path: '/disputes' });

    await screen.findByText('Shop o1');
    expect(screen.getByRole('heading', { level: 2, name: 'Open' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2, name: 'Closed' })).not.toBeInTheDocument();
  });

  it('shows the description, reason, day in South African time, amount and status on each row', async () => {
    serveDisputes([
      summary('o1', 'submitted', {
        reason: 'duplicateCharge',
        // 23:30 UTC on the 26th is 01:30 on the 27th in Johannesburg.
        createdAt: '2026-10-26T23:30:00Z',
        disputedAmount: { amount: '4299.00', currency: 'ZAR' },
      }),
    ]);
    renderApp({ path: '/disputes' });

    const row = (await screen.findByText('Shop o1')).closest('li') as HTMLElement;
    expect(row).toHaveTextContent('Duplicate charge, raised 27 Oct 2026');
    // Testing Library would fold U+202F into a plain space, so the text is matched as it is.
    expect(
      within(row).getByText('R4\u202F299.00', { normalizer: (text) => text }),
    ).toBeInTheDocument();
    expect(row).toHaveTextContent('Submitted');
    expect(within(row).getByText('27 Oct 2026')).toHaveAttribute(
      'datetime',
      '2026-10-26T23:30:00Z',
    );
  });

  it('links each row to its dispute, named by the description and the status', async () => {
    serveDisputes([summary('o1', 'underReview')]);
    const { user, router } = renderApp({ path: '/disputes' });

    const link = await screen.findByRole('link', { name: /Shop o1/ });
    expect(link).toHaveAccessibleName(expect.stringContaining('Under review'));
    expect(link).toHaveAttribute('href', '/disputes/o1');

    await user.click(link);
    expect(router.state.location.pathname).toBe('/disputes/o1');
  });

  it('leaves out an item whose status is outside the enum, and never says "Escalated"', async () => {
    serveDisputes([
      summary('x1', 'escalated' as CustomerStatus),
      summary('o1', 'submitted'),
      summary('c1', 'withdrawn'),
    ]);
    renderApp({ path: '/disputes' });

    await screen.findByText('Shop o1');
    expect(screen.queryByText('Shop x1')).not.toBeInTheDocument();
    expect(screen.getByText('Shop c1')).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/escalated/i);
  });

  it('renders no reason label, and never the raw value, for a reason outside the enum', async () => {
    serveDisputes([summary('o1', 'submitted', { reason: 'cosmicRays' as Reason })]);
    renderApp({ path: '/disputes' });

    const row = (await screen.findByText('Shop o1')).closest('li') as HTMLElement;
    expect(row).toHaveTextContent('Raised 30 Oct 2026');
    expect(row).not.toHaveTextContent(/cosmicRays/i);
  });

  it('shows the outcome badges for resolved disputes, and plain Resolved without an outcome', async () => {
    serveDisputes([
      summary('c1', 'resolved', { outcome: 'upheld' }),
      summary('c2', 'resolved', { outcome: 'rejected' }),
      summary('c3', 'resolved', { outcome: null }),
    ]);
    renderApp({ path: '/disputes' });

    await screen.findByText('Shop c1');
    expect(screen.getByRole('link', { name: /Shop c1.*Resolved · Upheld/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Shop c2.*Resolved · Rejected/ })).toBeInTheDocument();
    const plain = screen.getByRole('link', { name: /Shop c3/ });
    expect(plain).toHaveTextContent(/Resolved$/);
  });

  it('puts nothing in the URL or in storage', async () => {
    serveTwoPages([summary('o1', 'submitted')], [summary('o2', 'submitted')]);
    const { user, router } = renderApp({ path: '/disputes' });

    await user.click(await screen.findByRole('button', { name: 'Load more' }));
    await screen.findByText('Shop o2');

    expect(router.state.location.href).toBe('/disputes');
    expect(localStorage).toHaveLength(0);
    expect(sessionStorage).toHaveLength(0);
  });

  it('has no accessibility violations', async () => {
    serveDisputes([
      summary('o1', 'submitted'),
      summary('o2', 'underReviewPrioritised'),
      summary('c1', 'resolved', { outcome: 'upheld' }),
      summary('c2', 'resolved', { outcome: 'rejected' }),
      summary('c3', 'withdrawn'),
    ]);
    renderApp({ path: '/disputes' });
    await screen.findByText('Shop o1');

    expect(await axe(document.body)).toHaveNoViolations();
  });
});

// Criterion 3: Load more, focus and announcement.
describe('Load more', () => {
  const firstPage = [summary('o1', 'submitted'), summary('c1', 'withdrawn')];

  it('sends no limit, passes nextCursor back unchanged as cursor, and goes away on the last page', async () => {
    const queries = serveTwoPages(firstPage, [summary('o2', 'submitted')]);
    const { user } = renderApp({ path: '/disputes' });

    await user.click(await screen.findByRole('button', { name: 'Load more' }));

    expect(await screen.findByText('Shop o2')).toBeInTheDocument();
    expect(queries).toEqual(['', '?cursor=cursor-for-page-2']);
    expect(screen.getByText('Shop o1')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('shows Loading… as a focusable aria-disabled button and sends no second request on a second press', async () => {
    let requests = 0;
    server.use(
      http.get('*/api/v1/disputes', async ({ request }) => {
        requests += 1;
        if (new URL(request.url).searchParams.get('cursor') === null)
          return HttpResponse.json({ items: firstPage, nextCursor: 'cursor-for-page-2' });
        await delay(50);
        return HttpResponse.json({ items: [summary('o2', 'submitted')], nextCursor: null });
      }),
    );
    const { user } = renderApp({ path: '/disputes' });

    await user.click(await screen.findByRole('button', { name: 'Load more' }));
    const loading = await screen.findByRole('button', { name: 'Loading…' });
    expect(loading).toHaveAttribute('aria-disabled', 'true');
    expect(loading).not.toBeDisabled();
    await user.click(loading);

    await screen.findByText('Shop o2');
    expect(requests).toBe(2);
  });

  it('moves focus to the first new dispute by its ID, wherever it renders, and announces the new rows', async () => {
    // Both new rows are older than the first page (its rows carry the default 2026-10-30 createdAt). The first in
    // API order is the closed one, which renders in Closed; the open one is first in the Open section, so a
    // position-based focus would land on the wrong row.
    serveTwoPages(firstPage, [
      summary('c2', 'resolved', { createdAt: '2026-10-29T08:00:00Z' }),
      summary('o2', 'submitted', { createdAt: '2026-10-28T08:00:00Z' }),
    ]);
    const { user } = renderApp({ path: '/disputes' });

    await user.click(await screen.findByRole('button', { name: 'Load more' }));

    await screen.findByText('Shop c2');
    const firstNewRow = screen.getByText('Shop c2').closest('li');
    await waitFor(() => {
      expect(firstNewRow).toHaveFocus();
    });
    expect(firstNewRow).toHaveAttribute('tabindex', '-1');
    expect(sectionOf('Closed')).toContainElement(firstNewRow);
    // The shown order follows the API order across both pages: each section lists its newer page-one row, then
    // its older page-two row, with the older day shown.
    const rowsOf = (section: HTMLElement) =>
      within(section)
        .getAllByRole('link')
        .map((link) => [link.textContent, link.querySelector('time')?.getAttribute('datetime')]);
    expect(rowsOf(sectionOf('Open'))).toEqual([
      [expect.stringContaining('Shop o1'), '2026-10-30T08:00:00Z'],
      [expect.stringContaining('Shop o2'), '2026-10-28T08:00:00Z'],
    ]);
    expect(rowsOf(sectionOf('Closed'))).toEqual([
      [expect.stringContaining('Shop c1'), '2026-10-30T08:00:00Z'],
      [expect.stringContaining('Shop c2'), '2026-10-29T08:00:00Z'],
    ]);
    const liveRegion = screen.getByRole('status');
    expect(liveRegion).toHaveAttribute('aria-live', 'polite');
    expect(liveRegion).toHaveTextContent('2 more disputes loaded');
  });

  it('says "1 more dispute loaded" for a single new row', async () => {
    serveTwoPages(firstPage, [summary('o2', 'submitted')]);
    const { user } = renderApp({ path: '/disputes' });

    await user.click(await screen.findByRole('button', { name: 'Load more' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('1 more dispute loaded');
    });
  });

  it('keeps the loaded rows and the button, and shows the notice with the reference, when it fails', async () => {
    server.use(
      http.get('*/api/v1/disputes', ({ request }) =>
        new URL(request.url).searchParams.get('cursor') === null
          ? HttpResponse.json({ items: firstPage, nextCursor: 'cursor-for-page-2' })
          : problemResponse('internal', 500, 'trace-more'),
      ),
    );
    const { user } = renderApp({ path: '/disputes' });

    await user.click(await screen.findByRole('button', { name: 'Load more' }));

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Something went wrong on our side. Try again.');
    expect(notice).toHaveTextContent('trace-more');
    expect(screen.getByText('Shop o1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Load more' })).toBeInTheDocument();
  });

  // A refetch is not a page of Load more: it must not move focus or announce, however the last Load more ended.
  describe.each([
    {
      ending: 'failed',
      nextPage: () => problemResponse('internal', 500, 'trace-more'),
      settled: () => screen.findByRole('alert'),
    },
    {
      ending: 'brought only rows this build leaves out',
      nextPage: () =>
        HttpResponse.json({
          items: [summary('x1', 'escalated' as CustomerStatus)],
          nextCursor: null,
        }),
      settled: () =>
        waitFor(() => {
          expect(screen.queryByRole('button', { name: /^Load/ })).not.toBeInTheDocument();
        }),
    },
  ])('after a Load more that $ending', ({ nextPage, settled }) => {
    it('leaves focus alone and announces nothing when a later refetch brings a new dispute', async () => {
      let firstPageItems = firstPage;
      server.use(
        http.get('*/api/v1/disputes', ({ request }) =>
          new URL(request.url).searchParams.get('cursor') === null
            ? HttpResponse.json({ items: firstPageItems, nextCursor: 'cursor-for-page-2' })
            : nextPage(),
        ),
      );
      const { user, queryClient } = renderApp({ path: '/disputes' });
      await user.click(await screen.findByRole('button', { name: 'Load more' }));
      await settled();

      firstPageItems = [summary('o0', 'submitted'), ...firstPage];
      await queryClient.invalidateQueries();

      const newRow = (await screen.findByText('Shop o0')).closest('li');
      expect(newRow).not.toHaveFocus();
      expect(screen.getByRole('status')).toBeEmptyDOMElement();
    });
  });
});

// Criterion 4: loading, empty and error states.
describe('states', () => {
  it('shows the title at once and 5 busy skeleton rows while the first page loads', async () => {
    server.use(
      http.get('*/api/v1/disputes', async () => {
        await delay('infinite');
        return HttpResponse.json({ items: [], nextCursor: null });
      }),
    );
    renderApp({ path: '/disputes' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Disputes' })).toBeInTheDocument();
    const list = screen.getByRole('list', { name: 'Loading disputes' });
    expect(list).toHaveAttribute('aria-busy', 'true');
    expect(within(list).getAllByRole('listitem')).toHaveLength(5);
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('shows the empty state with a link to transactions', async () => {
    serveDisputes([]);
    const { user, router } = renderApp({ path: '/disputes' });

    expect(
      await screen.findByRole('heading', {
        level: 2,
        name: "You haven't disputed any transactions.",
      }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        'If something on your account looks wrong, find it in your transactions and choose Dispute. It will show here with its status.',
      ),
    ).toBeInTheDocument();
    expect(screen.queryByRole('heading', { level: 2, name: 'Open' })).not.toBeInTheDocument();
    expect(await axe(document.body)).toHaveNoViolations();

    await user.click(screen.getByRole('link', { name: 'Go to transactions' }));
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/transactions');
    });
  });

  it('shows the notice with the reference, never the problem title or detail, and Try again refetches', async () => {
    let fail = true;
    server.use(
      http.get('*/api/v1/disputes', () =>
        fail
          ? problemResponse('internal', 500, 'trace-first', {
              detail: 'stack trace at Foo.Bar',
              title: 'NullReferenceException',
            })
          : HttpResponse.json({ items: [summary('o1', 'submitted')], nextCursor: null }),
      ),
    );
    const { user } = renderApp({ path: '/disputes' });

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Something went wrong on our side. Try again.');
    expect(notice).toHaveTextContent('trace-first');
    expect(document.body).not.toHaveTextContent(/NullReferenceException|stack trace/);
    expect(await axe(document.body)).toHaveNoViolations();

    fail = false;
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Shop o1')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('moves focus to the heading after a successful Try again, and leaves it on Try again when it fails again', async () => {
    let fail = true;
    server.use(
      http.get('*/api/v1/disputes', () =>
        fail
          ? problemResponse('internal', 500)
          : HttpResponse.json({ items: [summary('o1', 'submitted')], nextCursor: null }),
      ),
    );
    const { user } = renderApp({ path: '/disputes' });
    const tryAgain = await screen.findByRole('button', { name: 'Try again' });

    await user.click(tryAgain);
    await waitFor(() => {
      expect(tryAgain).not.toHaveAttribute('disabled');
    });
    expect(tryAgain).toHaveFocus();

    fail = false;
    await user.click(tryAgain);

    await screen.findByText('Shop o1');
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1, name: 'Disputes' })).toHaveFocus();
    });
  });

  it('shows the notice after a network error', async () => {
    server.use(http.get('*/api/v1/disputes', () => HttpResponse.error()));
    renderApp({ path: '/disputes' });

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
