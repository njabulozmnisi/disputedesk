import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import {
  getGetApiV1DisputesInfiniteQueryKey,
  getGetApiV1TransactionsInfiniteQueryKey,
} from '@/api/generated/disputedesk';
import type { DisputeResponse, TimelineEntryResponse } from '@/api/generated/model';
import { server } from '@/test/server';
import {
  disputeGuid,
  makeDispute,
  problemResponse,
  renderApp,
  serveAntiforgeryToken,
  serveDispute,
  serveDisputes,
  serveTransactions,
  signInAs,
} from '@/test/support';

const notFoundHeading = "We can't find that dispute";
const rejectedGuidance =
  "If you disagree with this outcome, you can complain to the bank first. If you're still not satisfied after the bank has answered your complaint, you can take it to the National Financial Ombud Scheme South Africa, an independent service that is free for customers.";

beforeEach(() => {
  signInAs('linked');
  serveAntiforgeryToken();
  serveDisputes();
  serveTransactions();
});

function entry(overrides: Partial<TimelineEntryResponse>): TimelineEntryResponse {
  return {
    action: 'raise',
    customerStatus: 'submitted',
    actor: 'you',
    occurredAt: '2026-10-26T12:32:00Z',
    note: null,
    ...overrides,
  };
}

/** Opens the detail of `dispute` and waits for its h1, the transaction description. */
async function openDetail(dispute: DisputeResponse = makeDispute()) {
  serveDispute(dispute);
  const app = renderApp({ path: `/disputes/${dispute.disputeId}` });
  await screen.findByRole('heading', { level: 1, name: dispute.transaction.description });
  return app;
}

const h1 = () => screen.getByRole('heading', { level: 1 });

// A GUID with hex letters, so that its upper-case form differs from the API's lower-case one.
const lettersGuid = 'abcdef12-abcd-4bcd-8bcd-abcdef123456';

/**
 * Serves the detail GET and the withdraw POST of `lettersGuid` whatever the case of the GUID in the path, as the API
 * does (RFC 9562, section 4: the hex digits are case-insensitive). The bodies carry the lower-case ID, as the API
 * writes a Guid. Records the GET paths.
 */
function serveDisputeAnyCase(
  respondToWithdraw: () => Response,
  requestedDetailPaths: string[] = [],
) {
  const isTheDispute = (id: unknown) => String(id).toLowerCase() === lettersGuid;
  server.use(
    http.get('*/api/v1/disputes/:disputeId', ({ request, params }) => {
      requestedDetailPaths.push(new URL(request.url).pathname);
      if (!isTheDispute(params.disputeId)) return problemResponse('not-found', 404);
      return HttpResponse.json(makeDispute({ disputeId: lettersGuid }));
    }),
    http.post('*/api/v1/disputes/:disputeId/withdraw', ({ params }) => {
      if (!isTheDispute(params.disputeId)) return problemResponse('not-found', 404);
      return respondToWithdraw();
    }),
  );
}

// Criterion 1: the route takes only a GUID, and every not-found looks the same.
describe('the route parameter', () => {
  it.each([
    ['a word', '/disputes/abc'],
    ['an encoded path traversal', '/disputes/..%2Fx%3Fy%23z'],
    ['a GUID followed by an encoded traversal', `/disputes/${disputeGuid}%2F..`],
    ['a braced GUID', `/disputes/%7B${disputeGuid}%7D`],
    ['a GUID without hyphens', `/disputes/${disputeGuid.replaceAll('-', '')}`],
    ['a GUID with a trailing character', `/disputes/${disputeGuid}x`],
  ])('shows the not-found screen for %s and sends no request', async (_name, path) => {
    const requested: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requested.push(new URL(request.url).pathname);
    });
    renderApp({ path });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to your disputes' })).toBeInTheDocument();
    expect(requested.filter((pathname) => pathname.startsWith('/api/v1/disputes'))).toEqual([]);
  });

  it.each([
    ['a GUID the API answers 404', `/disputes/${disputeGuid}`],
    ['an unknown route', '/nowhere'],
  ])('shows the one not-found screen for %s', async (_name, path) => {
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, () => problemResponse('not-found', 404)),
    );
    renderApp({ path });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(screen.getByText('Check the link, or find it in your disputes.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Go to your disputes' })).toBeInTheDocument();
  });

  it('accepts an upper-case GUID and asks the API for its lower-case form', async () => {
    const requested: string[] = [];
    serveDisputeAnyCase(() => HttpResponse.json(withdrawn()), requested);
    renderApp({ path: `/disputes/${lettersGuid.toUpperCase()}` });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' }),
    ).toBeInTheDocument();
    expect(requested).toEqual([`/api/v1/disputes/${lettersGuid}`]);
  });

  it('sends the withdraw to the route value, not to the ID the response carries', async () => {
    const posted: string[] = [];
    server.use(
      http.get('*/api/v1/disputes/:disputeId', () =>
        HttpResponse.json(makeDispute({ disputeId: lettersGuid.toUpperCase() })),
      ),
      http.post('*/api/v1/disputes/:disputeId/withdraw', ({ request }) => {
        posted.push(new URL(request.url).pathname);
        return HttpResponse.json(withdrawn());
      }),
    );
    const { user } = renderApp({ path: `/disputes/${lettersGuid.toUpperCase()}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });

    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(posted).toEqual([`/api/v1/disputes/${lettersGuid}/withdraw`]);
    });
  });
});

// Criterion 2: summary, states and the Rejected guidance.
describe('loading, failing and loaded', () => {
  it('shows a skeleton header and 3 timeline entries under aria-busy while loading', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, async () => {
        await gate;
        return HttpResponse.json(makeDispute());
      }),
    );
    renderApp({ path: `/disputes/${disputeGuid}` });

    expect(await screen.findByRole('heading', { level: 1, name: 'Dispute' })).toBeInTheDocument();
    expect(document.title).toBe('Dispute · DisputeDesk');
    const busy = document.querySelector('[aria-busy="true"]');
    expect(busy).not.toBeNull();
    expect(busy?.querySelectorAll('li')).toHaveLength(3);

    const loadingHeading = h1();

    release();
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    expect(document.querySelector('[aria-busy="true"]')).toBeNull();
    // One element throughout, so the shell sees one screen (EXPERIENCE.md, Accessibility Floor).
    expect(h1()).toBe(loadingHeading);
  });

  it('makes the transaction description the h1, takes focus and sets the title', async () => {
    serveDispute();
    const app = renderApp({ path: '/disputes' });
    await screen.findByRole('heading', { level: 1, name: 'Disputes' });
    await app.router.navigate({ to: '/disputes/$disputeId', params: { disputeId: disputeGuid } });

    const heading = await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
    expect(document.title).toBe('ZXP*GLOBALSVCS 0800 · DisputeDesk');
  });

  it('shows the problem notice with the traceId and refetches on Try again', async () => {
    let calls = 0;
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, () => {
        calls += 1;
        return calls === 1
          ? problemResponse('internal', 500, 'trace-77')
          : HttpResponse.json(makeDispute());
      }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });

    expect(await screen.findByText('Something went wrong on our side. Try again.')).toBeVisible();
    expect(screen.getByText('trace-77')).toBeInTheDocument();
    expect(screen.queryByText('internal')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Try again' }));

    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    expect(calls).toBe(2);
  });

  it('moves focus to the heading after a successful Try again, as the button unmounts', async () => {
    let calls = 0;
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, () => {
        calls += 1;
        return calls === 1 ? problemResponse('internal', 500) : HttpResponse.json(makeDispute());
      }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByText('Something went wrong on our side. Try again.');

    await user.click(screen.getByRole('button', { name: 'Try again' }));

    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('checks the error before the data: a failed refetch leaves no stale detail', async () => {
    serveDetailSequence(
      makeDispute(),
      () => problemResponse('internal', 500, 'trace-78'),
      () => HttpResponse.json(makeDispute()),
    );
    const { user, queryClient } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });

    await act(() =>
      queryClient.invalidateQueries({ queryKey: [`/api/v1/disputes/${disputeGuid}`] }),
    );

    expect(await screen.findByText('Something went wrong on our side. Try again.')).toBeVisible();
    expect(screen.getByText('trace-78')).toBeInTheDocument();
    expect(screen.queryByText('Disputed amount')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Try again' }));

    await screen.findByText('Disputed amount');
    expect(screen.queryByText('Something went wrong on our side. Try again.')).toBeNull();
  });

  it('shows the not-found screen when a refetch of a loaded dispute is answered 404', async () => {
    serveDetailSequence(makeDispute(), () => problemResponse('not-found', 404));
    const { queryClient } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });

    await act(() =>
      queryClient.invalidateQueries({ queryKey: [`/api/v1/disputes/${disputeGuid}`] }),
    );

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(screen.queryByText('Disputed amount')).not.toBeInTheDocument();
  });

  it.each([
    ['a 5xx', () => problemResponse('internal', 500, 'trace-1')],
    ['another 4xx', () => problemResponse('validation', 400, 'trace-1')],
    ['a network error', () => HttpResponse.error()],
  ])('shows the problem notice for %s', async (_name, respond) => {
    server.use(http.get(`*/api/v1/disputes/${disputeGuid}`, respond));
    renderApp({ path: `/disputes/${disputeGuid}` });

    expect(await screen.findByText('Something went wrong on our side. Try again.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('shows the summary: status, amount, reason, date, masked card and a way back', async () => {
    await openDetail();

    expect(screen.getByText('Dispute status:')).toBeInTheDocument();
    expect(screen.getAllByText('Submitted').length).toBeGreaterThan(0);
    expect(screen.getByText('R849.00')).toBeInTheDocument();
    expect(screen.getByText('Unrecognised')).toBeInTheDocument();
    expect(screen.getByText('Mon 26 Oct 2026')).toBeInTheDocument();
    // The one printed form, as on the transaction list; no separate "Card" label repeats it.
    expect(screen.getByText('Card 492345•• •••• 3456')).toBeInTheDocument();
    expect(screen.getByText('card ending 3456')).toBeInTheDocument();
    expect(screen.queryByText('Card')).not.toBeInTheDocument();
    expect(screen.queryByText('Transaction amount')).not.toBeInTheDocument();
    expect(screen.queryByText('Expected amount')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to disputes' })).toHaveAttribute(
      'href',
      '/disputes',
    );
    expect(document.body).not.toHaveTextContent(disputeGuid);
  });

  it('shows the transaction and expected amounts only when the expected amount is set', async () => {
    await openDetail(
      makeDispute({
        reason: 'wrongAmount',
        expectedAmount: { amount: '749.00', currency: 'ZAR' },
        disputedAmount: { amount: '100.00', currency: 'ZAR' },
      }),
    );

    expect(screen.getByText('Transaction amount')).toBeInTheDocument();
    expect(screen.getByText('R849.00')).toBeInTheDocument();
    expect(screen.getByText('Expected amount')).toBeInTheDocument();
    expect(screen.getByText('R749.00')).toBeInTheDocument();
    expect(screen.getByText('R100.00')).toBeInTheDocument();
  });

  it('shows no label for an unknown reason, never the raw value', async () => {
    await openDetail(makeDispute({ reason: 'martian' as DisputeResponse['reason'] }));

    expect(screen.queryByText('martian')).not.toBeInTheDocument();
  });

  it('shows the Rejected guidance only for a rejected outcome', async () => {
    await openDetail(
      makeDispute({
        customerStatus: 'resolved',
        outcome: 'rejected',
        timeline: [
          entry({}),
          entry({ action: 'resolve', customerStatus: 'resolved', actor: 'theBank' }),
        ],
      }),
    );

    expect(screen.getByText(rejectedGuidance)).toBeInTheDocument();
  });

  it.each([
    ['upheld', 'upheld' as const],
    ['open', null],
  ])('shows no Rejected guidance for a %s dispute', async (_name, outcome) => {
    await openDetail(makeDispute({ customerStatus: outcome ? 'resolved' : 'submitted', outcome }));

    expect(screen.queryByText(/If you disagree with this outcome/)).not.toBeInTheDocument();
  });

  it('shows the "Dispute submitted" banner, not as a live region, only for a just-submitted entry', async () => {
    serveDispute();
    const app = renderApp({ path: '/disputes' });
    await screen.findByRole('heading', { level: 1, name: 'Disputes' });

    await app.router.navigate({
      to: '/disputes/$disputeId',
      params: { disputeId: disputeGuid },
      state: { justSubmitted: true },
    });

    const banner = await screen.findByText('Dispute submitted');
    expect(banner.closest('[role="status"], [role="alert"], [aria-live]')).toBeNull();
    expect(banner.compareDocumentPosition(h1()) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(app.router.state.location.search).toEqual({});
  });

  it('shows no banner when the entry was not a just-submitted one', async () => {
    await openDetail();

    expect(screen.queryByText('Dispute submitted')).not.toBeInTheDocument();
  });

  it('stores no dispute data in localStorage or sessionStorage', async () => {
    await openDetail();

    expect(localStorage).toHaveLength(0);
    expect(sessionStorage).toHaveLength(0);
  });

  it('has no accessibility violations on a rejected dispute', async () => {
    await openDetail(
      makeDispute({
        customerStatus: 'resolved',
        outcome: 'rejected',
        timeline: [
          entry({}),
          entry({ action: 'resolve', customerStatus: 'resolved', actor: 'theBank' }),
        ],
      }),
    );
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('shows the Account line with the last four digits when the transaction has no card', async () => {
    await openDetail(
      makeDispute({
        transaction: { ...makeDispute().transaction, maskedCard: null, accountLast4: '4821' },
      }),
    );

    expect(screen.getByText('Account •••• 4821')).toBeInTheDocument();
    expect(screen.getByText('account ending 4821')).toBeInTheDocument();
    expect(screen.queryByText('Account')).not.toBeInTheDocument();
    expect(screen.queryByText('Card')).not.toBeInTheDocument();
  });

  it('has no accessibility violations on the not-found state', async () => {
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, () => problemResponse('not-found', 404)),
    );
    renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: notFoundHeading });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('has no accessibility violations while loading', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, async () => {
        await gate;
        return HttpResponse.json(makeDispute());
      }),
    );
    renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'Dispute' });
    expect(document.querySelector('[aria-busy="true"]')).not.toBeNull();

    expect(await axe(document.body)).toHaveNoViolations();

    release();
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
  });

  it('has no accessibility violations on the failure', async () => {
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, () => problemResponse('internal', 500)),
    );
    renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByText('Something went wrong on our side. Try again.');

    expect(await axe(document.body)).toHaveNoViolations();
  });
});

// Criterion 3: the timeline as the customer sees it.
describe('the timeline', () => {
  it('is an ordered list in API order, each entry with its status, line, actor and time', async () => {
    await openDetail(
      makeDispute({
        customerStatus: 'resolved',
        outcome: 'upheld',
        timeline: [
          entry({}),
          entry({
            action: 'startReview',
            customerStatus: 'underReview',
            actor: 'theBank',
            occurredAt: '2026-10-27T08:00:00Z',
          }),
          entry({
            action: 'escalate',
            customerStatus: 'underReviewPrioritised',
            actor: 'theBank',
            occurredAt: '2026-10-28T08:00:00Z',
          }),
          entry({
            action: 'resolve',
            customerStatus: 'resolved',
            actor: 'theBank',
            occurredAt: '2026-10-29T08:00:00Z',
          }),
        ],
      }),
    );

    const list = within(screen.getByRole('region', { name: 'Timeline' })).getByRole('list');
    expect(list.tagName).toBe('OL');
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(4);
    expect(items[0]).toHaveTextContent('Submitted');
    expect(items[0]).toHaveTextContent('You submitted this dispute.');
    expect(items[0]).toHaveTextContent('You');
    expect(items[1]).toHaveTextContent('Under review');
    expect(items[1]).toHaveTextContent('The bank started reviewing your dispute.');
    expect(items[1]).toHaveTextContent('The bank');
    expect(items[2]).toHaveTextContent('Under review (prioritised)');
    expect(items[2]).toHaveTextContent('The bank prioritised the review of your dispute.');
    expect(items[3]).toHaveTextContent('Resolved · Upheld');
    expect(items[3]).toHaveTextContent('The bank agreed with you.');
    expect(document.body).not.toHaveTextContent('Escalated');
  });

  it('words a rejected resolve and a withdraw', async () => {
    await openDetail(
      makeDispute({
        customerStatus: 'resolved',
        outcome: 'rejected',
        timeline: [
          entry({ action: 'resolve', customerStatus: 'resolved', actor: 'theBank' }),
          entry({ action: 'withdraw', customerStatus: 'withdrawn', actor: 'you' }),
        ],
      }),
    );

    expect(screen.getByText("The bank didn't uphold your dispute.")).toBeInTheDocument();
    expect(screen.getByText('Resolved · Rejected', { selector: 'li *' })).toBeInTheDocument();
    expect(screen.getByText('You withdrew this dispute.')).toBeInTheDocument();
  });

  it('shows the time in South African time inside <time> holding the API instant', async () => {
    await openDetail(
      makeDispute({
        timeline: [
          entry({ occurredAt: '2026-10-26T12:32:00Z' }),
          entry({ action: 'startReview', occurredAt: '2026-09-04T07:05:00Z' }),
        ],
      }),
    );

    const october = screen.getByText('26 Oct 2026, 14:32 SAST');
    expect(october.tagName).toBe('TIME');
    expect(october).toHaveAttribute('datetime', '2026-10-26T12:32:00Z');
    const september = screen.getByText('4 Sep 2026, 09:05 SAST');
    expect(september).toHaveAttribute('datetime', '2026-09-04T07:05:00Z');
  });

  it('shows a note in quotes as plain text, with no autolinking', async () => {
    await openDetail(
      makeDispute({
        timeline: [entry({ note: 'See https://evil.example/x and <b>bold</b>' })],
      }),
    );

    expect(screen.getByText('“See https://evil.example/x and <b>bold</b>”')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /evil/ })).not.toBeInTheDocument();
    expect(document.querySelector('b')).toBeNull();
  });

  it('ends an open dispute with the promise to update, after the list and not as an entry in it', async () => {
    await openDetail();

    const promise = screen.getByText("We'll update the timeline as your dispute moves.");
    expect(promise.tagName).toBe('P');
    expect(promise.closest('li')).toBeNull();
    expect(promise.previousElementSibling?.tagName).toBe('OL');
    expect(
      within(screen.getByRole('region', { name: 'Timeline' })).getAllByRole('listitem'),
    ).toHaveLength(1);
  });

  it('does not end a closed dispute with the promise', async () => {
    await openDetail(makeDispute({ customerStatus: 'withdrawn' }));

    expect(
      screen.queryByText("We'll update the timeline as your dispute moves."),
    ).not.toBeInTheDocument();
  });

  it('renders no line or label for an unknown action, status, actor or outcome, never the raw value', async () => {
    await openDetail(
      makeDispute({
        customerStatus: 'resolved',
        outcome: 'mystery' as DisputeResponse['outcome'],
        timeline: [
          entry({ action: 'teleport' as TimelineEntryResponse['action'] }),
          entry({ customerStatus: 'escalated' as TimelineEntryResponse['customerStatus'] }),
          entry({ actor: 'robot' as TimelineEntryResponse['actor'] }),
          entry({ action: 'resolve', customerStatus: 'resolved', actor: 'theBank' }),
        ],
      }),
    );

    for (const raw of ['teleport', 'escalated', 'Escalated', 'robot', 'mystery']) {
      expect(document.body).not.toHaveTextContent(raw);
    }
    expect(screen.queryByText('The bank agreed with you.')).not.toBeInTheDocument();
    expect(screen.queryByText("The bank didn't uphold your dispute.")).not.toBeInTheDocument();
  });
});

// Criterion 4: Withdraw is offered only while open.
describe('the Withdraw button', () => {
  it.each([
    ['submitted', true],
    ['underReview', true],
    ['underReviewPrioritised', true],
    ['resolved', false],
    ['withdrawn', false],
    ['teleported', false],
  ])('for %s: shown is %s', async (customerStatus, shown) => {
    await openDetail(
      makeDispute({ customerStatus: customerStatus as DisputeResponse['customerStatus'] }),
    );

    expect(screen.queryByRole('button', { name: 'Withdraw dispute' }) !== null).toBe(shown);
  });
});

/** Opens the withdraw dialog from the detail screen. */
async function openWithdrawDialog(user: ReturnType<typeof renderApp>['user']) {
  await user.click(screen.getByRole('button', { name: 'Withdraw dispute' }));
  return screen.findByRole('alertdialog', { name: 'Withdraw this dispute?' });
}

const withdrawn = () =>
  makeDispute({
    customerStatus: 'withdrawn',
    timeline: [
      entry({}),
      entry({
        action: 'withdraw',
        customerStatus: 'withdrawn',
        occurredAt: '2026-10-30T09:00:00Z',
      }),
    ],
  });

/** The detail GET answers `first` once, then what `then` returns once, then what `later` returns (default: `then`). */
function serveDetailSequence(first: DisputeResponse, then: () => Response, later = then) {
  let calls = 0;
  server.use(
    http.get(`*/api/v1/disputes/${disputeGuid}`, () => {
      calls += 1;
      if (calls === 1) return HttpResponse.json(first);
      return calls === 2 ? then() : later();
    }),
  );
}

function serveWithdraw(respond: (attempt: number) => Response | Promise<Response>) {
  const requests: { url: string; headers: Headers; body: unknown }[] = [];
  server.use(
    http.post(`*/api/v1/disputes/${disputeGuid}/withdraw`, async ({ request }) => {
      requests.push({ url: request.url, headers: request.headers, body: await request.json() });
      return respond(requests.length);
    }),
  );
  return requests;
}

describe('the withdraw dialog', () => {
  it('is an alert dialog with its warning, optional note and two buttons, focus on Keep dispute', async () => {
    const { user } = await openDetail();

    const dialog = await openWithdrawDialog(user);

    expect(
      within(dialog).getByText("You can't dispute this transaction again after you withdraw."),
    ).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note (optional)')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Withdraw dispute' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Keep dispute' })).toHaveFocus();
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('keeps focus inside while open', async () => {
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);

    await user.tab();
    await user.tab();
    await user.tab();
    await user.tab();

    expect(dialog).toContainElement(document.activeElement as HTMLElement);
  });

  it.each([
    ['Escape', (user: ReturnType<typeof renderApp>['user']) => user.keyboard('{Escape}')],
    [
      'Keep dispute',
      (user: ReturnType<typeof renderApp>['user']) =>
        user.click(screen.getByRole('button', { name: 'Keep dispute' })),
    ],
  ])('closes on %s and returns focus to Withdraw dispute', async (_name, close) => {
    const { user } = await openDetail();
    await openWithdrawDialog(user);

    await close(user);

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: 'Withdraw dispute' })).toHaveFocus();
  });

  it('while a request is pending, ignores Escape and Keep dispute, and marks Keep aria-disabled', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    serveWithdraw(async () => {
      await gate;
      return HttpResponse.json(withdrawn());
    });
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));
    const keep = within(dialog).getByRole('button', { name: 'Keep dispute' });
    expect(keep).toHaveAttribute('aria-disabled', 'true');
    await user.keyboard('{Escape}');
    await user.click(keep);

    expect(screen.getByRole('alertdialog')).toBeInTheDocument();

    release();
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
  });
});

// Criterion 5: the note and the pending confirm.
describe('the note and the confirm', () => {
  it('counts the trimmed note and announces only at 450 and at 500', async () => {
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);
    const note = within(dialog).getByLabelText('Note (optional)');
    await user.click(note);

    await user.paste('a'.repeat(100));
    expect(within(dialog).getByText('100 / 500')).toBeInTheDocument();
    expect(screen.getByRole('status')).toBeEmptyDOMElement();

    await user.paste('a'.repeat(350));
    expect(within(dialog).getByText('450 / 500')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('50 characters left');

    await user.paste('a'.repeat(50));
    expect(screen.getByRole('status')).toHaveTextContent(
      "You've reached the limit of 500 characters",
    );
  });

  it('refuses 501 on confirm with the error under the field and in a summary that takes focus, and sends nothing', async () => {
    const requests = serveWithdraw(() => HttpResponse.json(withdrawn()));
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByLabelText('Note (optional)'));
    await user.paste('a'.repeat(501));

    // Past the limit is allowed in the field, never cut.
    expect(within(dialog).getByLabelText('Note (optional)')).toHaveValue('a'.repeat(501));
    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(within(dialog).getAllByText('Your note must be 500 characters or fewer.')).toHaveLength(
      2,
    );
    expect(within(dialog).getByRole('group', { name: 'Check the details below' })).toHaveFocus();
    expect(within(dialog).getByLabelText('Note (optional)')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    expect(requests).toHaveLength(0);
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('accepts 500 units, a 500-unit note padded with spaces, and whitespace only', async () => {
    const requests = serveWithdraw(() => HttpResponse.json(withdrawn()));
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByLabelText('Note (optional)'));
    await user.paste(`  ${'a'.repeat(500)}   `);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ note: 'a'.repeat(500) });
  });

  it('sends {} for a whitespace-only note', async () => {
    const requests = serveWithdraw(() => HttpResponse.json(withdrawn()));
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByLabelText('Note (optional)'));
    await user.paste('   \n  ');

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({});
  });

  it('sends the trimmed note in the body with the antiforgery header, no Idempotency-Key and nothing in the URL', async () => {
    const requests = serveWithdraw(() => HttpResponse.json(withdrawn()));
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByLabelText('Note (optional)'));
    await user.paste('  Found it on my statement.  ');

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    const [request] = requests;
    expect(request?.body).toEqual({ note: 'Found it on my statement.' });
    expect(request?.headers.get('x-xsrf-token')).toBe('request-token');
    expect(request?.headers.has('idempotency-key')).toBe(false);
    expect(new URL(request?.url ?? '').search).toBe('');
    expect(request?.url).not.toContain('statement');
  });

  it('shows "Withdrawing…" with aria-disabled while pending, and a second press sends nothing', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const requests = serveWithdraw(async () => {
      await gate;
      return HttpResponse.json(withdrawn());
    });
    const { user } = await openDetail();
    const dialog = await openWithdrawDialog(user);
    const confirm = within(dialog).getByRole('button', { name: 'Withdraw dispute' });

    // Two presses in one tick: the second is blocked before the first renders.
    fireEvent.click(confirm);
    fireEvent.click(confirm);

    const pending = await within(dialog).findByRole('button', { name: 'Withdrawing…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    await user.click(pending);
    release();
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(requests).toHaveLength(1);
  });
});

/**
 * Criterion 2 after a failed refetch: the failure notice with Try again stands in place of the loaded card and the
 * dialog, and focus is on the heading or on Try again, never on the body (WCAG 2.2 SC 2.4.3).
 */
async function expectFailureInPlaceOfDetail(traceId: string) {
  expect(await screen.findByText(traceId)).toBeInTheDocument();
  const tryAgain = screen.getByRole('button', { name: 'Try again' });
  expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  expect(screen.queryByText('Disputed amount')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
  await waitFor(() => {
    expect([h1(), tryAgain]).toContain(document.activeElement);
  });
}

// Criterion 6: the withdraw outcomes.
describe('the withdraw outcomes', () => {
  it('on 200 writes the response into the cache, announces, closes and focuses the h1 without a detail refetch', async () => {
    serveDetailSequence(makeDispute(), () => problemResponse('internal', 500));
    serveWithdraw(() => HttpResponse.json(withdrawn()));
    const { user, queryClient } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    queryClient.setQueryData(getGetApiV1DisputesInfiniteQueryKey(), { pages: [], pageParams: [] });
    queryClient.setQueryData(getGetApiV1TransactionsInfiniteQueryKey(), {
      pages: [],
      pageParams: [],
    });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(screen.getByText('Withdrawn', { selector: 'span span' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
    expect(screen.getByRole('status')).toHaveTextContent('Dispute withdrawn');
    expect(queryClient.getQueryState(getGetApiV1DisputesInfiniteQueryKey())?.isInvalidated).toBe(
      true,
    );
    expect(
      queryClient.getQueryState(getGetApiV1TransactionsInfiniteQueryKey())?.isInvalidated,
    ).toBe(true);
  });

  it('for an upper-case GUID in the URL, shows Withdrawn after the withdraw, as the API answers with the lower-case ID', async () => {
    serveDisputeAnyCase(() => HttpResponse.json({ ...withdrawn(), disputeId: lettersGuid }));
    const { user } = renderApp({ path: `/disputes/${lettersGuid.toUpperCase()}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(screen.getByText('Withdrawn', { selector: 'span span' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
  });

  it('on 409 with customerStatus withdrawn, awaits the detail refetch, then closes as a success', async () => {
    serveDetailSequence(makeDispute(), () => HttpResponse.json(withdrawn()));
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'withdrawn' }),
    );
    const { user, queryClient } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    queryClient.setQueryData(getGetApiV1DisputesInfiniteQueryKey(), { pages: [], pageParams: [] });
    queryClient.setQueryData(getGetApiV1TransactionsInfiniteQueryKey(), {
      pages: [],
      pageParams: [],
    });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(queryClient.getQueryState(getGetApiV1DisputesInfiniteQueryKey())?.isInvalidated).toBe(
      true,
    );
    expect(
      queryClient.getQueryState(getGetApiV1TransactionsInfiniteQueryKey())?.isInvalidated,
    ).toBe(true);
    expect(screen.getAllByText('Withdrawn').length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Dispute withdrawn');
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('on 409 with customerStatus resolved, keeps the dialog open with the notice after the refetch removes the button', async () => {
    serveDetailSequence(makeDispute(), () =>
      HttpResponse.json(makeDispute({ customerStatus: 'resolved', outcome: 'upheld' })),
    );
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'resolved' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(
      await within(dialog).findByText("This dispute is now Resolved. It can't be withdrawn."),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getAllByText('Resolved · Upheld', { exact: false }).length).toBeGreaterThan(0);
    });
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    expect(
      screen.getByText("This dispute is now Resolved. It can't be withdrawn."),
    ).toBeInTheDocument();
    // Closing it later puts focus on the h1: the Withdraw button is gone.
    await user.click(within(dialog).getByRole('button', { name: 'Keep dispute' }));
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('on 409 with an open customerStatus, shows the generic notice with the traceId, keeps the note and refetches', async () => {
    let detailCalls = 0;
    server.use(
      http.get(`*/api/v1/disputes/${disputeGuid}`, () => {
        detailCalls += 1;
        return HttpResponse.json(makeDispute({ customerStatus: 'underReview' }));
      }),
    );
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-409', { customerStatus: 'underReview' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByLabelText('Note (optional)'));
    await user.paste('keep me');

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(
      await within(dialog).findByText('Something went wrong on our side. Try again.'),
    ).toBeInTheDocument();
    expect(within(dialog).getByText('trace-409')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note (optional)')).toHaveValue('keep me');
    await waitFor(() => {
      expect(detailCalls).toBe(2);
    });
  });

  it('on 409 with customerStatus resolved and a failing detail refetch, closes the dialog and shows the failure, then the status on Try again', async () => {
    serveDetailSequence(
      makeDispute(),
      () => problemResponse('internal', 500, 'trace-get'),
      () => HttpResponse.json(makeDispute({ customerStatus: 'resolved', outcome: 'upheld' })),
    );
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'resolved' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await expectFailureInPlaceOfDetail('trace-get');
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByText('Disputed amount');
    expect(screen.getAllByText('Resolved · Upheld', { exact: false }).length).toBeGreaterThan(0);
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('on 409 with an open customerStatus and a failing detail refetch, closes the dialog and shows the failure, then the status on Try again', async () => {
    serveDetailSequence(
      makeDispute(),
      () => problemResponse('internal', 500, 'trace-get'),
      () => HttpResponse.json(makeDispute({ customerStatus: 'underReview' })),
    );
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-409', { customerStatus: 'underReview' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByLabelText('Note (optional)'));
    await user.paste('keep me');

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await expectFailureInPlaceOfDetail('trace-get');
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    await screen.findByText('Disputed amount');
    expect(screen.getAllByText('Under review', { exact: false }).length).toBeGreaterThan(0);
    expect(screen.getByRole('button', { name: 'Withdraw dispute' })).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('on 409 with customerStatus withdrawn and a failing detail refetch, closes the dialog and shows the failure, then Withdrawn on Try again', async () => {
    serveDetailSequence(
      makeDispute(),
      () => problemResponse('internal', 500, 'trace-get'),
      () => HttpResponse.json(withdrawn()),
    );
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'withdrawn' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await expectFailureInPlaceOfDetail('trace-get');
    await user.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText('Withdrawn', { selector: 'span span' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Withdraw dispute' })).not.toBeInTheDocument();
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('after a conflict and a failing detail refetch, focus is on the heading when the screen was reached by a route change', async () => {
    serveDetailSequence(makeDispute(), () => problemResponse('internal', 500, 'trace-get'));
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'resolved' }),
    );
    const app = renderApp({ path: '/disputes' });
    await screen.findByRole('heading', { level: 1, name: 'Disputes' });
    await app.router.navigate({ to: '/disputes/$disputeId', params: { disputeId: disputeGuid } });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(app.user);

    await app.user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    await expectFailureInPlaceOfDetail('trace-get');
  });

  it('on 409 with customerStatus resolved and a detail refetch answered 404, closes the dialog and shows the not-found screen', async () => {
    serveDetailSequence(makeDispute(), () => problemResponse('not-found', 404));
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'resolved' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('on 409 with an unknown customerStatus, says it cannot be withdrawn any more', async () => {
    serveDispute();
    serveWithdraw(() =>
      problemResponse('action-not-allowed', 409, 'trace-1', { customerStatus: 'teleported' }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(
      await within(dialog).findByText("This dispute can't be withdrawn any more."),
    ).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent('teleported');
  });

  it('on 400 validation naming note, shows the note error and the summary', async () => {
    serveDispute();
    serveWithdraw(() =>
      problemResponse('validation', 400, 'trace-1', { errors: { note: ['too long'] } }),
    );
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(
      await within(dialog).findAllByText('Your note must be 500 characters or fewer.'),
    ).toHaveLength(2);
    expect(within(dialog).getByRole('group', { name: 'Check the details below' })).toHaveFocus();
    expect(document.body).not.toHaveTextContent('too long');
  });

  it('on 404, closes the dialog and shows the not-found screen', async () => {
    serveDispute();
    serveWithdraw(() => problemResponse('not-found', 404));
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);

    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await waitFor(() => {
      expect(h1()).toHaveFocus();
    });
  });

  it('on 404, shows not-found for that dispute only, not for the next one opened', async () => {
    const otherGuid = '22222222-2222-4222-8222-222222222222';
    serveDispute();
    serveDispute(makeDispute({ disputeId: otherGuid }));
    serveWithdraw(() => problemResponse('not-found', 404));
    const { user, router } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));
    await screen.findByRole('heading', { level: 1, name: notFoundHeading });

    await router.navigate({ to: '/disputes/$disputeId', params: { disputeId: otherGuid } });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' }),
    ).toBeInTheDocument();
  });

  it.each([
    ['a 5xx', () => problemResponse('internal', 500, 'trace-500')],
    ['another 4xx', () => problemResponse('internal', 418, 'trace-500')],
    [
      'a 400 not naming note',
      () => problemResponse('validation', 400, 'trace-500', { errors: { other: ['x'] } }),
    ],
    ['a network error', () => HttpResponse.error()],
  ])(
    'on %s, shows the generic notice and keeps the dialog and the note',
    async (_name, respond) => {
      serveDispute();
      serveWithdraw(respond);
      const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
      await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
      const dialog = await openWithdrawDialog(user);
      await user.click(within(dialog).getByLabelText('Note (optional)'));
      await user.paste('still here');

      await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));

      expect(
        await within(dialog).findByText('Something went wrong on our side. Try again.'),
      ).toBeInTheDocument();
      expect(within(dialog).getByLabelText('Note (optional)')).toHaveValue('still here');
      expect(within(dialog).getByRole('button', { name: 'Withdraw dispute' })).not.toHaveAttribute(
        'aria-disabled',
        'true',
      );
    },
  );

  it('has no accessibility violations with a notice in the dialog', async () => {
    serveDispute();
    serveWithdraw(() => problemResponse('internal', 500, 'trace-1'));
    const { user } = renderApp({ path: `/disputes/${disputeGuid}` });
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    const dialog = await openWithdrawDialog(user);
    await user.click(within(dialog).getByRole('button', { name: 'Withdraw dispute' }));
    await within(dialog).findByText('Something went wrong on our side. Try again.');

    expect(await axe(document.body)).toHaveNoViolations();
  });
});

// Criterion 7: typed links.
describe('links to the detail', () => {
  it('follows the transactions badge to the detail without a page load', async () => {
    serveDispute();
    serveTransactions([
      {
        transactionId: 'TXN-1',
        accountRef: 'ACC-1',
        accountLast4: '4821',
        transactionDate: '2026-10-26T09:14:00Z',
        description: 'ZXP*GLOBALSVCS 0800',
        amount: { amount: '849.00', currency: 'ZAR' },
        direction: 'debit',
        kind: 'card',
        state: 'settled',
        maskedCard: '492345******3456',
        disputable: false,
        notDisputableReason: 'alreadyDisputed',
        disputeId: disputeGuid,
        customerStatus: 'submitted',
      },
    ]);
    const { user, router } = renderApp({ path: '/transactions' });

    const badge = await screen.findByRole('link', { name: 'Dispute status: Submitted' });
    expect(badge).toHaveAttribute('href', `/disputes/${disputeGuid}`);
    await user.click(badge);

    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
  });
});
