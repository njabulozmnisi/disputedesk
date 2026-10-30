import type { SpanContext } from '@opentelemetry/api';
import { act, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import { getApiV1DisputesDisputeId, getApiV1Transactions } from '@/api/generated/disputedesk';
import {
  chooseReason,
  continueToReview,
  openRaiseScreen,
  openReviewScreen,
  recordRaiseRequests,
} from '@/test/raise';
import { server } from '@/test/server';
import { endedSpans, spansNamed } from '@/test/spans';
import {
  disputeGuid,
  failWith,
  makeDispute,
  makeRow,
  problemResponse,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

// Story 3.11: the raise flow's spans (AD-20), read from an in-memory exporter with MSW as the API.

const submit = () => screen.getByRole('button', { name: /^Submit/ });
const tryAgain = () => screen.getByRole('button', { name: 'Try again' });
const openSpans = () => spansNamed('dispute.raise.open');
const submitSpans = () => spansNamed('dispute.raise.submit');
const postSpans = () => spansNamed('POST');
const dispute = '/transactions/$transactionId/dispute';

async function passSeconds(seconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(seconds * 1000);
  });
}

function traceparentsOf(requests: { headers: Headers }[]) {
  return requests.map((request) => request.headers.get('traceparent'));
}

// Criterion 3: one submit span per attempt, parent of every POST.
describe('the submit span', () => {
  describe('with resends', () => {
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    it('is one span, the parent of all three POSTs, for two in-progress answers and a 201', async () => {
      const requests = recordRaiseRequests((attempt) =>
        attempt < 3
          ? problemResponse('idempotency-in-progress', 409)
          : HttpResponse.json(makeDispute(), { status: 201 }),
      );
      const { user } = await openReviewScreen({ fakeTimers: true });

      await user.click(submit());
      await waitFor(() => {
        expect(requests).toHaveLength(1);
      });
      await passSeconds(1);
      await waitFor(() => {
        expect(requests).toHaveLength(2);
      });
      await passSeconds(1);
      await waitFor(() => {
        expect(submitSpans()).toHaveLength(1);
      });

      const [submitSpan] = submitSpans();
      expect(postSpans()).toHaveLength(3);
      for (const post of postSpans()) {
        expect(post.parentSpanContext?.spanId).toBe(submitSpan?.spanContext().spanId);
        expect(post.spanContext().traceId).toBe(submitSpan?.spanContext().traceId);
      }
      for (const traceparent of traceparentsOf(requests)) {
        expect(traceparent).toContain(`-${submitSpan?.spanContext().traceId ?? ''}-`);
      }
    });

    it('ends with outcome idempotency-in-progress and the last status when the resends run out', async () => {
      const requests = recordRaiseRequests(() => problemResponse('idempotency-in-progress', 409));
      const { user } = await openReviewScreen({ fakeTimers: true });

      await user.click(submit());
      for (let resend = 1; resend <= 5; resend += 1) {
        await waitFor(() => {
          expect(requests).toHaveLength(resend);
        });
        await passSeconds(1);
      }

      await waitFor(() => {
        expect(submitSpans()).toHaveLength(1);
      });
      expect(requests).toHaveLength(6);
      expect(submitSpans()[0]?.attributes).toEqual({
        'http.response.status_code': 409,
        'dispute.raise.outcome': 'idempotency-in-progress',
      });
    });
  });

  it('makes a second submit span, in its own trace, for Try again', async () => {
    const requests = recordRaiseRequests((attempt) =>
      attempt === 1
        ? problemResponse('core-banking-unavailable', 503)
        : HttpResponse.json(makeDispute(), { status: 201 }),
    );
    const { user } = await openReviewScreen();
    await user.click(submit());
    await screen.findByRole('button', { name: 'Try again' });

    await user.click(tryAgain());

    await waitFor(() => {
      expect(submitSpans()).toHaveLength(2);
    });
    expect(requests).toHaveLength(2);
    const [first, second] = submitSpans();
    expect(first?.spanContext().traceId).not.toBe(second?.spanContext().traceId);
    expect(first?.attributes['dispute.raise.outcome']).toBe('core-banking-unavailable');
    expect(second?.attributes['dispute.raise.outcome']).toBe('submitted');
  });

  const outcomes: [string, () => Response, number | undefined, string][] = [
    ['submitted', () => HttpResponse.json(makeDispute(), { status: 201 }), 201, 'submitted'],
    ['already-disputed', () => problemResponse('already-disputed', 409), 409, 'already-disputed'],
    ['not-disputable', () => problemResponse('not-disputable', 409), 409, 'not-disputable'],
    ['not-found', () => problemResponse('not-found', 404), 404, 'not-found'],
    ['validation', () => problemResponse('validation', 400), 400, 'validation'],
    [
      'idempotency-mismatch',
      () => problemResponse('idempotency-mismatch', 422),
      422,
      'idempotency-mismatch',
    ],
    [
      'core-banking-unavailable',
      () => problemResponse('core-banking-unavailable', 503),
      503,
      'core-banking-unavailable',
    ],
    ['an unlisted problem', () => problemResponse('linking-required', 403), 403, 'unexpected'],
    [
      'a body that is not a problem',
      () => new HttpResponse('<html></html>', { status: 502 }),
      502,
      'unexpected',
    ],
    ['a network error', () => HttpResponse.error(), undefined, 'network-error'],
  ];

  it.each(outcomes)(
    'ends with the right outcome and status for %s',
    async (_name, respond, status, outcome) => {
      recordRaiseRequests(respond);
      const { user } = await openReviewScreen();

      await user.click(submit());

      await waitFor(() => {
        expect(submitSpans()).toHaveLength(1);
      });
      const expected: Record<string, unknown> = { 'dispute.raise.outcome': outcome };
      if (status !== undefined) expected['http.response.status_code'] = status;
      expect(submitSpans()[0]?.attributes).toEqual(expected);
    },
  );

  it('ends as unexpected, with the antiforgery status, when the antiforgery fetch fails', async () => {
    const requests = recordRaiseRequests();
    const { user } = await openReviewScreen();
    failWith('/api/v1/bff/antiforgery', 'unauthorized', 401);

    await user.click(submit());

    await waitFor(() => {
      expect(submitSpans()).toHaveLength(1);
    });
    expect(requests).toHaveLength(0);
    expect(submitSpans()[0]?.attributes).toEqual({
      'http.response.status_code': 401,
      'dispute.raise.outcome': 'unexpected',
    });
  });
});

// Criterion 4: one open span per admitted entry, linked from each submit.
describe('the open span', () => {
  const entry = { strictMode: true };

  it('is recorded once for an admitted row, under StrictMode', async () => {
    await openRaiseScreen([makeRow()], entry);

    expect(openSpans()).toHaveLength(1);
    expect(openSpans()[0]?.attributes).toEqual({});
  });

  it('is not recorded for a deep link or a reload, which have no cached row', async () => {
    signInAs('linked');
    serveTransactions([makeRow()]);
    renderApp({ path: '/transactions/TXN-1/dispute', ...entry });

    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    expect(openSpans()).toHaveLength(0);
  });

  it('is not recorded for a row that is not disputable', async () => {
    signInAs('linked');
    serveTransactions([makeRow({ disputable: false, notDisputableReason: 'notEligible' })]);
    const { router } = renderApp({ path: '/transactions', ...entry });
    await screen.findByText('ZXP*GLOBALSVCS 0800');

    await act(() => router.navigate({ to: dispute, params: { transactionId: 'TXN-1' } }));

    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(openSpans()).toHaveLength(0);
  });

  it('is not recorded again by Back after a submit', async () => {
    recordRaiseRequests();
    const { user, router } = await openReviewScreen(entry);
    await user.click(submit());
    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/disputes/${disputeGuid}`);
    });

    act(() => {
      router.history.back();
    });

    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(openSpans()).toHaveLength(1);
  });

  it('is still one after 4a, 4b and 4a again', async () => {
    const { user } = await openReviewScreen(entry);

    await user.click(screen.getByRole('link', { name: 'Change' }));

    await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
    expect(openSpans()).toHaveLength(1);
  });

  it('is recorded a second time when the customer leaves to /transactions and enters again', async () => {
    const { user } = await openReviewScreen(entry);
    await user.click(screen.getByRole('link', { name: 'Cancel' }));
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    await user.click(await screen.findByRole('link', { name: 'Dispute this transaction' }));

    await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
    expect(openSpans()).toHaveLength(2);
  });

  it('is linked, without link attributes, from each submit span of its flow', async () => {
    recordRaiseRequests((attempt) =>
      attempt === 1
        ? problemResponse('core-banking-unavailable', 503)
        : HttpResponse.json(makeDispute(), { status: 201 }),
    );
    const { user } = await openReviewScreen(entry);
    await user.click(submit());
    await user.click(await screen.findByRole('button', { name: 'Try again' }));
    await waitFor(() => {
      expect(submitSpans()).toHaveLength(2);
    });

    const open: SpanContext | undefined = openSpans()[0]?.spanContext();
    expect(openSpans()).toHaveLength(1);
    for (const span of submitSpans()) {
      expect(span.links).toHaveLength(1);
      expect(span.links[0]?.context.spanId).toBe(open?.spanId);
      expect(span.links[0]?.context.traceId).toBe(open?.traceId);
      expect(span.links[0]?.attributes ?? {}).toEqual({});
    }
  });
});

// Criterion 5: no personal data in any span.
describe('the exported spans', () => {
  const guid = '0b9a1c7e-5d3f-4a62-9e1b-7c2d8f4a6b10';
  const seeded = [
    'TXN-SEED-7Q4Z',
    'ACC-SEED-9K2M',
    'SEEDSHOP 4417',
    'note-seed-5V8X',
    '123.45',
    '987.65',
    guid,
    'trace-seed-3J6P',
    'cursor-seed-8L1N',
  ];
  const seedRow = () =>
    makeRow({
      transactionId: 'TXN-SEED-7Q4Z',
      accountRef: 'ACC-SEED-9K2M',
      description: 'SEEDSHOP 4417',
      amount: { amount: '987.65', currency: 'ZAR' },
    });

  const fetchAttributeKeys = [
    'http.request.method',
    'http.request.method_original',
    'url.full',
    'server.address',
    'server.port',
    'http.response.status_code',
    'error.type',
  ];

  /** The seeded values flow through the API calls the app makes; the cursor and the GUID path are fetched directly. */
  async function fetchCursorAndDispute() {
    server.use(
      http.get('*/api/v1/transactions', () =>
        HttpResponse.json({ items: [seedRow()], nextCursor: null }),
      ),
      http.get(`*/api/v1/disputes/${guid}`, () =>
        HttpResponse.json(makeDispute({ disputeId: guid })),
      ),
    );
    await getApiV1Transactions({ cursor: 'cursor-seed-8L1N' });
    await getApiV1DisputesDisputeId(guid);
  }

  async function fillAndSubmit(user: Awaited<ReturnType<typeof openRaiseScreen>>['user']) {
    await chooseReason(user, 'Wrong amount');
    await user.type(screen.getByLabelText('Expected amount'), '123.45');
    await user.click(screen.getByLabelText('Note (optional)'));
    await user.paste('note-seed-5V8X');
    await continueToReview(user);
    await user.click(submit());
  }

  function expectNothingSeeded(keys: string[]) {
    const everything = JSON.stringify(
      endedSpans().map((span) => ({
        name: span.name,
        attributes: span.attributes,
        events: span.events,
        links: span.links,
        resource: span.resource.attributes,
      })),
    );
    for (const value of [...seeded, ...keys]) expect(everything).not.toContain(value);
  }

  function expectAllowedAttributesOnly() {
    for (const span of endedSpans()) {
      const keys = Object.keys(span.attributes);
      if (span.name === 'dispute.raise.open') expect(keys).toEqual([]);
      else if (span.name === 'dispute.raise.submit') {
        expect(
          keys.every((key) => ['http.response.status_code', 'dispute.raise.outcome'].includes(key)),
        ).toBe(true);
      } else expect(keys.every((key) => fetchAttributeKeys.includes(key))).toBe(true);
      expect(span.events).toEqual([]);
      for (const link of span.links) expect(link.attributes ?? {}).toEqual({});
      expect(
        Object.keys(span.resource.attributes).every(
          (key) => key === 'service.name' || key.startsWith('telemetry.sdk.'),
        ),
      ).toBe(true);
      expect(span.attributes['http.url']).toBeUndefined();
    }
  }

  function expectFetchUrlsAreSanitized() {
    const urls = endedSpans()
      .map((span) => span.attributes['url.full'])
      .filter(Boolean) as string[];
    expect(urls.length).toBeGreaterThan(0);
    for (const url of urls) {
      expect(url).not.toContain('?');
      expect(url).not.toContain('#');
    }
    expect(urls).toContain(`${window.location.origin}/api/v1/disputes/{id}`);
  }

  it('hold no seeded value after a sign-in, a list, a raise to 201 and a dispute fetch', async () => {
    const requests = recordRaiseRequests(() =>
      HttpResponse.json(makeDispute({ disputeId: guid }), { status: 201 }),
    );
    signInAs('linked');
    serveAntiforgeryToken();
    serveTransactions([seedRow()]);
    const app = renderApp({ path: '/transactions', strictMode: true });
    await app.user.click(await screen.findByRole('link', { name: 'Dispute this transaction' }));
    await fillAndSubmit(app.user);
    await waitFor(() => {
      expect(app.router.state.location.pathname).toBe(`/disputes/${guid}`);
    });
    await fetchCursorAndDispute();

    expect(submitSpans()).toHaveLength(1);
    expectNothingSeeded([requests[0]?.headers.get('Idempotency-Key') ?? 'missing']);
    expectAllowedAttributesOnly();
    expectFetchUrlsAreSanitized();
  });

  it('hold no seeded value after a raise that ends in a 409 already-disputed', async () => {
    const requests = recordRaiseRequests(() =>
      problemResponse('already-disputed', 409, 'trace-seed-3J6P', { disputeId: guid }),
    );
    signInAs('linked');
    serveAntiforgeryToken();
    serveTransactions([seedRow()]);
    const app = renderApp({ path: '/transactions', strictMode: true });
    await app.user.click(await screen.findByRole('link', { name: 'Dispute this transaction' }));
    await fillAndSubmit(app.user);
    await screen.findByText(/trace-seed-3J6P/);
    await fetchCursorAndDispute();

    expect(submitSpans()[0]?.attributes['dispute.raise.outcome']).toBe('already-disputed');
    expectNothingSeeded([requests[0]?.headers.get('Idempotency-Key') ?? 'missing']);
    expectAllowedAttributesOnly();
    expectFetchUrlsAreSanitized();
  });

  it('holds the sanitized URL even when the request fails', async () => {
    server.use(http.get('*/api/v1/transactions', () => HttpResponse.error()));

    await getApiV1Transactions({ cursor: 'cursor-seed-8L1N' }).catch(() => undefined);

    const [span] = spansNamed('GET');
    expect(span?.attributes['url.full']).toBe(`${window.location.origin}/api/v1/transactions`);
    expectNothingSeeded([]);
  });
});
