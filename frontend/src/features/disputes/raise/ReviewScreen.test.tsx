import { act, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import { getGetApiV1TransactionsInfiniteQueryKey } from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  chooseReason,
  created,
  continueToReview,
  openRaiseScreen,
  openReviewScreen,
  recordRaiseRequests,
  type RaiseRequest,
} from '@/test/raise';
import { disputeGuid, makeDispute, makeRow, problemResponse, serveDispute } from '@/test/support';

const quotedUuid = /^"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"$/;
const submit = () => screen.getByRole('button', { name: /^Submit/ });
const tryAgain = () => screen.getByRole('button', { name: 'Try again' });

/** Each request carried `X-XSRF-TOKEN` and the same quoted key. */
function expectOneKeyAndToken(requests: RaiseRequest[]) {
  const keys = requests.map((request) => request.headers.get('Idempotency-Key'));
  expect(new Set(keys).size).toBe(1);
  expect(keys[0]).toMatch(quotedUuid);
  for (const request of requests) {
    expect(request.headers.get('X-XSRF-TOKEN')).toBe('request-token');
  }
}

/** Moves the faked timers, which are the retry delays. */
async function passSeconds(seconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(seconds * 1000);
  });
}

// Criterion 5: one draft, one request, one key.
describe('the review screen', () => {
  it('reads the draft back, and Change returns to 4a with the values kept', async () => {
    const { user } = await openRaiseScreen([
      makeRow({ amount: { amount: '849.00', currency: 'ZAR' } }),
    ]);
    await chooseReason(user, 'Wrong amount');
    await user.type(screen.getByLabelText('Expected amount'), '80.00');
    await user.click(screen.getByLabelText('Note (optional)'));
    await user.paste('  never bought this  ');
    await continueToReview(user);

    const summary = screen.getByRole('main');
    expect(within(summary).getByText("You're disputing")).toBeInTheDocument();
    expect(within(summary).getByText('R769.00')).toBeInTheDocument();
    expect(within(summary).getByText('ZXP*GLOBALSVCS 0800')).toBeInTheDocument();
    expect(within(summary).getByText('Mon 26 Oct 2026')).toBeInTheDocument();
    expect(within(summary).getByText('Card 492345•• •••• 3456')).toBeInTheDocument();
    expect(within(summary).getAllByText(/Card/)).toHaveLength(1);
    expect(within(summary).getByText('Wrong amount')).toBeInTheDocument();
    expect(within(summary).getByText('never bought this')).toBeInTheDocument();

    expect(await axe(document.body)).toHaveNoViolations();

    await user.click(screen.getByRole('link', { name: 'Change' }));

    await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
    expect(screen.getByRole('radio', { name: 'Wrong amount' })).toBeChecked();
    expect(screen.getByLabelText('Expected amount')).toHaveValue('80.00');
    expect(screen.getByLabelText('Note (optional)')).toHaveValue('  never bought this  ');
  });

  it('sends the frozen request with a quoted key and the antiforgery token, the amount as a string', async () => {
    const requests = recordRaiseRequests();
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');
    await user.type(screen.getByLabelText('Expected amount'), ' 80.00 ');
    await continueToReview(user);

    await user.click(submit());

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({
      accountRef: 'ACC-1',
      transactionId: 'TXN-1',
      reason: 'wrongAmount',
      note: null,
      expectedAmount: '80.00',
    });
    expectOneKeyAndToken(requests);
  });

  it('mints the key from crypto.randomUUID and sends it quoted (RFC 9651 String)', async () => {
    vi.spyOn(crypto, 'randomUUID').mockReturnValue('11111111-2222-4333-8444-555555555555');
    const requests = recordRaiseRequests();
    const { user } = await openReviewScreen();

    await user.click(submit());

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.headers.get('Idempotency-Key')).toBe(
      '"11111111-2222-4333-8444-555555555555"',
    );
  });

  it('keeps the key when Continue builds an equal request, and mints a new one for a different request', async () => {
    const requests = recordRaiseRequests(() => problemResponse('internal', 504));
    const { user } = await openReviewScreen();
    await user.click(submit());
    await screen.findByText(/We couldn't confirm your dispute was received/);

    await user.click(screen.getByRole('link', { name: 'Change' }));
    await continueToReview(user);
    await user.click(submit());
    await waitFor(() => {
      expect(requests).toHaveLength(2);
    });
    expect(requests[1]?.headers.get('Idempotency-Key')).toBe(
      requests[0]?.headers.get('Idempotency-Key'),
    );

    await user.click(screen.getByRole('link', { name: 'Change' }));
    await user.click(screen.getByLabelText('Note (optional)'));
    await user.paste('now with a note');
    await continueToReview(user);
    await user.click(submit());
    await waitFor(() => {
      expect(requests).toHaveLength(3);
    });
    expect(requests[2]?.headers.get('Idempotency-Key')).not.toBe(
      requests[0]?.headers.get('Idempotency-Key'),
    );
    expect(requests[2]?.headers.get('Idempotency-Key')).toMatch(quotedUuid);
  });

  it('shows Submitting…, sets aria-disabled, keeps focus and ignores presses while an attempt is pending', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const requests: unknown[] = [];
    server.use(
      http.post('*/api/v1/disputes', async () => {
        requests.push(1);
        await gate;
        return HttpResponse.json({}, { status: 500 });
      }),
    );
    const { user } = await openReviewScreen();

    submit().focus();
    await user.click(submit());

    const pending = await screen.findByRole('button', { name: 'Submitting…' });
    expect(pending).toHaveAttribute('aria-disabled', 'true');
    expect(pending).not.toBeDisabled();
    expect(pending).toHaveFocus();
    await user.click(pending);
    await user.keyboard('{Enter}');
    expect(requests).toHaveLength(1);
    release();
  });

  it('sends once when Submit is pressed twice before the screen re-renders', async () => {
    const requests = recordRaiseRequests();
    await openReviewScreen();

    act(() => {
      submit().click();
      submit().click();
    });

    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    expect(requests).toHaveLength(1);
  });

  it('makes Change and Cancel aria-disabled and inert while an attempt is pending', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('*/api/v1/disputes', async () => {
        await gate;
        return HttpResponse.json({}, { status: 500 });
      }),
    );
    const { user, router } = await openReviewScreen();

    await user.click(submit());
    await screen.findByRole('button', { name: 'Submitting…' });

    for (const name of ['Change', 'Cancel']) {
      const link = screen.getByRole('link', { name });
      expect(link).toHaveAttribute('aria-disabled', 'true');
      expect(link).toHaveClass('aria-disabled:opacity-70', 'aria-disabled:cursor-progress');
      await user.click(link);
      expect(router.state.location.pathname).toBe('/transactions/TXN-1/dispute/review');
    }

    release();
    await screen.findByRole('button', { name: 'Try again' });
    expect(screen.getByRole('link', { name: 'Change' })).not.toHaveAttribute('aria-disabled');
    await user.click(screen.getByRole('link', { name: 'Cancel' }));
    expect(router.state.location.pathname).toBe('/transactions');
  });

  it('keeps the draft out of the URL, router state, history, the query cache and the console', async () => {
    const consoleCalls = (['log', 'info', 'warn', 'error', 'debug'] as const).map((method) =>
      vi.spyOn(console, method),
    );
    const requests = recordRaiseRequests();
    const { user, router, queryClient } = await openRaiseScreen();
    await chooseReason(user, 'Unrecognised');
    await user.click(screen.getByLabelText('Note (optional)'));
    await user.paste('a private sentence');
    await continueToReview(user);
    await user.click(submit());
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });

    const everywhere = JSON.stringify([
      router.state.location,
      router.history.location,
      queryClient
        .getQueryCache()
        .getAll()
        .map((query) => query.queryKey),
    ]);
    expect(everywhere).not.toContain('a private sentence');
    // The submit key stays out; the dispute's own ID is in the URL by design.
    const submitKey = (requests[0]?.headers.get('idempotency-key') ?? '').replaceAll('"', '');
    expect(submitKey).toMatch(/^[0-9a-f-]{36}$/);
    expect(everywhere).not.toContain(submitKey);
    for (const call of consoleCalls) expect(call).not.toHaveBeenCalled();
    expect(localStorage).toHaveLength(0);
    expect(sessionStorage).toHaveLength(0);
  });
});

// Criterion 7: submitted.
describe('after a 201', () => {
  it('invalidates the lists, announces, and replaces the entry with the dispute', async () => {
    const requests = recordRaiseRequests();
    const { user, router, queryClient } = await openRaiseScreen();
    queryClient.setQueryData(['infinite', '/api/v1/disputes'], { pages: [], pageParams: [] });
    queryClient.setQueryData(['/api/v1/disputes', 'D-7'], {});
    queryClient.setQueryData(['/api/v1/other'], {});
    await chooseReason(user, 'Unrecognised');
    await continueToReview(user);

    await user.click(submit());

    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });
    expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
    expect(router.state.location.state.justSubmitted).toBe(true);
    expect(screen.getByText('Dispute submitted', { selector: 'strong' })).toBeInTheDocument();
    expect(requests).toHaveLength(1);
    expect(screen.getByRole('status')).toHaveTextContent('Dispute submitted');
    const stateOf = (key: readonly unknown[]) => queryClient.getQueryState(key)?.isInvalidated;
    expect(stateOf(getGetApiV1TransactionsInfiniteQueryKey())).toBe(true);
    expect(stateOf(['infinite', '/api/v1/disputes'])).toBe(true);
    expect(stateOf(['/api/v1/disputes', 'D-7'])).toBe(true);
    expect(stateOf(['/api/v1/other'])).toBe(false);
  });

  it('sends Back from the dispute to /transactions, never to 4a or 4b', async () => {
    recordRaiseRequests();
    const { user, router } = await openReviewScreen();
    await user.click(submit());
    await screen.findByRole('heading', { level: 1, name: 'ZXP*GLOBALSVCS 0800' });

    act(() => {
      router.history.back();
    });

    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(router.state.location.pathname).toBe('/transactions');
  });

  it('treats a replayed 201 the same way', async () => {
    const replayed = makeDispute({ disputeId: '22222222-2222-4222-8222-222222222222' });
    serveDispute(replayed);
    recordRaiseRequests(() => HttpResponse.json(replayed, { status: 201 }));
    const { user, router } = await openReviewScreen();

    await user.click(submit());

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/disputes/22222222-2222-4222-8222-222222222222');
    });
  });

  it('after Back, a late 201 still invalidates and marks the draft submitted, but does not navigate', async () => {
    let release: () => void = () => undefined;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.post('*/api/v1/disputes', async () => {
        await gate;
        return created();
      }),
    );
    const { user, router, queryClient } = await openReviewScreen();
    queryClient.setQueryData(['/api/v1/disputes', 'D-7'], {});
    await user.click(submit());
    await screen.findByRole('button', { name: 'Submitting…' });

    act(() => {
      router.history.back();
    });
    await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
    release();

    // The submitted draft sends the raise routes to /transactions; the dispute is never opened.
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(router.state.location.pathname).toBe('/transactions');
    // The list is active again on /transactions and refetches at once, so check a query that stays idle.
    expect(queryClient.getQueryState(['/api/v1/disputes', 'D-7'])?.isInvalidated).toBe(true);
  });
});

// Criterion 6: retries keep the request and key.
describe('retries', () => {
  beforeEach(() => {
    // Only the timers are faked; the clock still ticks so promises and MSW keep moving.
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('resends after a 409 idempotency-in-progress once a second, keeping Submitting…, until the 201', async () => {
    const requests = recordRaiseRequests((attempt) =>
      attempt < 3 ? problemResponse('idempotency-in-progress', 409) : created(),
    );
    const { user, router } = await openReviewScreen({ fakeTimers: true });

    await user.click(submit());
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(screen.getByRole('button', { name: 'Submitting…' })).toBeInTheDocument();
    await passSeconds(1);
    await waitFor(() => {
      expect(requests).toHaveLength(2);
    });
    await passSeconds(1);

    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
    });
    expect(requests).toHaveLength(3);
    expectOneKeyAndToken(requests);
  });

  it('after the fifth resend gives the unconfirmed notice; Try again sends the same body and key', async () => {
    const requests = recordRaiseRequests((attempt) =>
      attempt <= 6 ? problemResponse('idempotency-in-progress', 409) : created(),
    );
    const { user, router } = await openReviewScreen({ fakeTimers: true });

    await user.click(submit());
    for (let resend = 1; resend <= 5; resend += 1) {
      await waitFor(() => {
        expect(requests).toHaveLength(resend);
      });
      await passSeconds(1);
    }

    await screen.findByText(
      "We couldn't confirm your dispute was received. Try again. You won't create a second dispute.",
    );
    expect(requests).toHaveLength(6);
    await user.click(tryAgain());

    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
    });
    expect(requests).toHaveLength(7);
    expectOneKeyAndToken(requests);
    expect(new Set(requests.map((request) => JSON.stringify(request.body))).size).toBe(1);
  });
});

describe('an attempt that ends without a final answer', () => {
  it('on a network error gives the unconfirmed notice; Try again resends the same key', async () => {
    const requests = recordRaiseRequests((attempt) =>
      attempt === 1 ? HttpResponse.error() : created(),
    );
    const { user, router } = await openReviewScreen();

    await user.click(submit());

    await screen.findByText(
      "We couldn't confirm your dispute was received. Try again. You won't create a second dispute.",
    );
    await user.click(tryAgain());
    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
    });
    expect(requests).toHaveLength(2);
    expectOneKeyAndToken(requests);
  });

  it('on a 504 gives the unconfirmed notice', async () => {
    recordRaiseRequests(() => problemResponse('internal', 504));
    const { user } = await openReviewScreen();

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      "We couldn't confirm your dispute was received. Try again. You won't create a second dispute.",
    );
    expect(tryAgain()).toBeInTheDocument();
  });

  it('on a 503 says the bank cannot be checked, with Try again and the same key', async () => {
    const requests = recordRaiseRequests((attempt) =>
      attempt === 1 ? problemResponse('core-banking-unavailable', 503) : created(),
    );
    const { user, router } = await openReviewScreen();

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      "We can't check this transaction with the bank right now. Try again.",
    );
    await user.click(tryAgain());
    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
    });
    expectOneKeyAndToken(requests);
  });

  it('on a bare 503 (not core-banking-unavailable) gives the unexpected-error notice', async () => {
    recordRaiseRequests(() => HttpResponse.json({}, { status: 503 }));
    const { user } = await openReviewScreen();

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(tryAgain()).toBeInTheDocument();
  });

  it('on any other 5xx says something went wrong, with Try again and the reference', async () => {
    recordRaiseRequests(() => problemResponse('internal', 500, 'trace-500'));
    const { user } = await openReviewScreen();

    await user.click(submit());

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Something went wrong on our side. Try again.');
    expect(notice).toHaveTextContent('trace-500');
    expect(tryAgain()).toBeInTheDocument();
  });
});

// Criterion 8: refusals and a lost session.
describe('a refused submit', () => {
  const backToTransactions = () => screen.getByRole('link', { name: 'Back to transactions' });

  it('409 already-disputed links to the dispute from the body and refreshes the list', async () => {
    recordRaiseRequests(() =>
      problemResponse('already-disputed', 409, 'trace-1', {
        disputeId: 'D-9',
        customerStatus: 'submitted',
      }),
    );
    const { user, queryClient } = await openReviewScreen();

    await user.click(submit());

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent(
      'This transaction already has a dispute. You can only dispute a transaction once.',
    );
    expect(notice).not.toHaveTextContent('already-disputed');
    expect(screen.getByRole('link', { name: 'View dispute' })).toHaveAttribute(
      'href',
      '/disputes/D-9',
    );
    expect(backToTransactions()).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    expect(
      queryClient.getQueryState(getGetApiV1TransactionsInfiniteQueryKey())?.isInvalidated,
    ).toBe(true);
  });

  it.each([
    ['outsideWindow', "It's now older than 90 days. Contact the bank about this transaction."],
    ['notEligible', 'Contact the bank about this transaction.'],
  ])('422 not-disputable (%s) explains it, and refreshes the list', async (reason, extra) => {
    recordRaiseRequests(() => problemResponse('not-disputable', 422, 'trace-1', { reason }));
    const { user, queryClient } = await openReviewScreen();

    await user.click(submit());

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent("This transaction can't be disputed any more.");
    expect(notice).toHaveTextContent(extra);
    expect(backToTransactions()).toBeInTheDocument();
    expect(
      queryClient.getQueryState(getGetApiV1TransactionsInfiniteQueryKey())?.isInvalidated,
    ).toBe(true);
  });

  it('404 says the transaction is no longer available', async () => {
    recordRaiseRequests(() => problemResponse('not-found', 404));
    const { user } = await openReviewScreen();

    await user.click(submit());

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This transaction is no longer available.',
    );
    expect(backToTransactions()).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1, name: 'Review your dispute' })).toHaveFocus();
    });
  });

  it('422 idempotency-mismatch shows the unexpected-error notice with the reference and no Try again', async () => {
    recordRaiseRequests(() => problemResponse('idempotency-mismatch', 422, 'trace-mismatch'));
    const { user } = await openReviewScreen();

    await user.click(submit());

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Something went wrong on our side. Try again.');
    expect(notice).toHaveTextContent('trace-mismatch');
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
    expect(backToTransactions()).toBeInTheDocument();
  });

  it('shows only the copy and the reference, never the title or detail of the body', async () => {
    recordRaiseRequests(() =>
      HttpResponse.json(
        {
          type: 'https://github.com/njabulozmnisi/disputedesk/blob/main/docs/problems/not-found.md',
          title: 'A secret title',
          detail: 'A secret detail with account 123',
          traceId: 'trace-1',
        },
        { status: 404, headers: { 'content-type': 'application/problem+json' } },
      ),
    );
    const { user } = await openReviewScreen();

    await user.click(submit());

    await screen.findByRole('alert');
    expect(screen.queryByText(/secret/)).not.toBeInTheDocument();
  });

  it('401 goes to the session-ended screen, whose Sign in again returns to /transactions', async () => {
    recordRaiseRequests(() => problemResponse('unauthorized', 401));
    const { user } = await openReviewScreen();

    await user.click(submit());

    const link = await screen.findByRole('link', { name: 'Sign in again' });
    expect(link).toHaveAttribute('href', '/api/v1/bff/login?returnUrl=%2Ftransactions');
  });
});
