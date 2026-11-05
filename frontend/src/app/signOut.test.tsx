import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import {
  getGetApiV1BffSessionQueryKey,
  getGetApiV1TransactionsInfiniteQueryKey,
} from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  failWith,
  makeRow,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

type SubmittedForm = {
  method: string;
  action: string;
  token: string | null;
  isInDocument: boolean;
};

/** jsdom cannot navigate, so form.submit() is replaced by a recorder. */
function recordFormSubmissions(): SubmittedForm[] {
  const submitted: SubmittedForm[] = [];
  vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(function (
    this: HTMLFormElement,
  ) {
    submitted.push({
      method: this.method,
      action: new URL(this.action).pathname,
      token: new FormData(this).get('__RequestVerificationToken') as string | null,
      isInDocument: this.isConnected,
    });
  });
  return submitted;
}

beforeEach(() => {
  signInAs('linked');
  serveTransactions([makeRow({ description: 'PRIVATE MERCHANT 4321' })]);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

// Criterion 4: sign-out is a form POST carrying a fresh token.
describe('Sign out', () => {
  it('fetches a fresh token, then submits a top-level form POST to the BFF logout with it', async () => {
    const calls: string[] = [];
    server.events.on('request:start', ({ request }) => {
      calls.push(`${request.method} ${new URL(request.url).pathname}`);
    });
    let issued = 0;
    server.use(
      http.get('*/api/v1/bff/antiforgery', () => {
        issued += 1;
        return HttpResponse.json({ requestToken: `token-${String(issued)}` });
      }),
    );
    const submitted = recordFormSubmissions();
    const { user } = renderApp({ path: '/transactions' });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(submitted).toHaveLength(1);
    });
    expect(submitted[0]).toEqual({
      method: 'post',
      action: '/api/v1/bff/logout',
      token: 'token-1',
      isInDocument: true,
    });
    expect(calls).toContain('GET /api/v1/bff/antiforgery');
    expect(calls).not.toContain('POST /api/v1/bff/logout'); // the browser navigates; the SPA sends no fetch
  });

  it('leaves the screen and its data alone until the token arrives', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('*/api/v1/bff/antiforgery', async () => {
        await held;
        return HttpResponse.json({ requestToken: 'token-1' });
      }),
    );
    const submitted = recordFormSubmissions();
    const { user, router, queryClient } = renderApp({ path: '/transactions' });
    await screen.findByText('PRIVATE MERCHANT 4321');

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(router.state.location.pathname).toBe('/transactions');
    expect(queryClient.getQueryData(getGetApiV1TransactionsInfiniteQueryKey())).toBeDefined();
    expect(submitted).toHaveLength(0);

    release();
    await waitFor(() => {
      expect(submitted).toHaveLength(1);
    });
  });

  it.each([
    [
      'a 5xx',
      () => {
        failWith('/api/v1/bff/antiforgery', 'core-banking-unavailable', 503);
      },
    ],
    [
      'no response',
      () => {
        server.use(http.get('*/api/v1/bff/antiforgery', () => HttpResponse.error()));
      },
    ],
  ])(
    'when the token fetch fails with %s: the data stays on screen under the notice, nothing is submitted, and Sign out tries again',
    async (_name, failTokenFetch) => {
      failTokenFetch();
      const submitted = recordFormSubmissions();
      const { user, router, queryClient } = renderApp({ path: '/transactions' });
      await screen.findByText('PRIVATE MERCHANT 4321');

      await user.click(screen.getByRole('button', { name: 'Sign out' }));

      const notice = await screen.findByRole('alert');
      expect(notice).toHaveTextContent("We couldn't finish signing you out.");
      expect(notice).toHaveTextContent('Try again.');
      expect(router.state.location.pathname).toBe('/transactions');
      expect(screen.getByText('PRIVATE MERCHANT 4321')).toBeInTheDocument();
      expect(queryClient.getQueryData(getGetApiV1BffSessionQueryKey())).toEqual({
        state: 'linked',
      });
      expect(submitted).toHaveLength(0);

      serveAntiforgeryToken('retry-token');
      // The retry belongs to the notice (EXPERIENCE.md, Problem notice), so it sits inside the alert.
      await user.click(within(notice).getByRole('button', { name: 'Sign out' }));
      await waitFor(() => {
        expect(submitted).toHaveLength(1);
      });
      expect(submitted[0]?.token).toBe('retry-token');
    },
  );

  it('leaves the document, with no notice, when the token fetch answers 401', async () => {
    const replace = vi.fn();
    vi.stubGlobal('location', { pathname: '/transactions', search: '', replace });
    failWith('/api/v1/bff/antiforgery', 'unauthorized', 401);
    const submitted = recordFormSubmissions();
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByText('PRIVATE MERCHANT 4321');

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(submitted).toHaveLength(0);
  });

  it('is available to an unlinked login', async () => {
    signInAs('unlinked');
    serveAntiforgeryToken('unlinked-token');
    const submitted = recordFormSubmissions();
    const { user } = renderApp({ path: '/no/such/page' });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(submitted[0]?.token).toBe('unlinked-token');
    });
  });
});
