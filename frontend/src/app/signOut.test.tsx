import { screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import {
  getGetSessionQueryKey,
  getGetApiV1TransactionsInfiniteQueryKey,
} from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  failWith,
  listenToTabs,
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

// Criterion 4: sign-out is a form POST carrying a fresh token.
describe('Sign out', () => {
  it('fetches a fresh token, then submits a top-level form POST to the sign-out with it', async () => {
    const calls: string[] = [];
    server.events.on('request:start', ({ request }) => {
      calls.push(`${request.method} ${new URL(request.url).pathname}`);
    });
    let issued = 0;
    server.use(
      http.get('*/api/v1/antiforgery', () => {
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
      action: '/api/v1/authentication/logout',
      token: 'token-1',
      isInDocument: true,
    });
    expect(calls).toContain('GET /api/v1/antiforgery');
    expect(calls).not.toContain('POST /api/v1/authentication/logout'); // the browser navigates; the SPA sends no fetch
  });

  it('clears customer data and shows the start page before the token is fetched, then posts signed-out and submits', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('*/api/v1/antiforgery', async () => {
        await held;
        return HttpResponse.json({ requestToken: 'token-1' });
      }),
    );
    const submitted = recordFormSubmissions();
    const tabs = listenToTabs();
    const { user, router, queryClient } = renderApp({ path: '/transactions' });
    await screen.findByText('PRIVATE MERCHANT 4321');
    await tabs.settle();

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/');
    });
    expect(queryClient.getQueryData(getGetApiV1TransactionsInfiniteQueryKey())).toBeUndefined();
    expect(queryClient.getQueryData(getGetSessionQueryKey())).toEqual({
      state: 'anonymous',
    });
    await tabs.flush();
    expect(tabs.messages).toEqual([]); // nothing posted until the token is fetched
    expect(submitted).toHaveLength(0);

    release();
    await waitFor(() => {
      expect(submitted).toHaveLength(1);
    });
    await tabs.flush();
    expect(tabs.messages).toEqual(['signed-out']);
    tabs.close();
  });

  it.each([
    [
      'a 5xx',
      () => {
        failWith('/api/v1/antiforgery', 'core-banking-unavailable', 503);
      },
    ],
    [
      'no response',
      () => {
        server.use(http.get('*/api/v1/antiforgery', () => HttpResponse.error()));
      },
    ],
  ])(
    'when the token fetch fails with %s: nothing comes back, nothing is posted, the other tab keeps its session, and Sign out tries again',
    async (_name, failTokenFetch) => {
      failTokenFetch();
      const submitted = recordFormSubmissions();
      const tabs = listenToTabs();
      const other = renderApp({ path: '/transactions' });
      await screen.findAllByText('PRIVATE MERCHANT 4321');
      const rowsPerTab = screen.getAllByText('PRIVATE MERCHANT 4321').length;
      const { user, router, queryClient } = renderApp({ path: '/transactions' });
      await waitFor(() => {
        expect(screen.getAllByText('PRIVATE MERCHANT 4321')).toHaveLength(2 * rowsPerTab);
      });
      await tabs.settle();

      await user.click(screen.getAllByRole('button', { name: 'Sign out' })[1] as HTMLElement);

      const notice = await screen.findByRole('alert');
      expect(notice).toHaveTextContent("We couldn't finish signing you out.");
      expect(notice).toHaveTextContent('Try again.');
      expect(router.state.location.pathname).toBe('/');
      expect(queryClient.getQueryData(getGetApiV1TransactionsInfiniteQueryKey())).toBeUndefined();
      expect(submitted).toHaveLength(0);
      await tabs.flush();
      // Both instances share this test's one module state, so the other instance posts activity for this tab's
      // failed response; two real tabs would not.
      expect(tabs.messages.filter((message) => message !== 'activity')).toEqual([]);
      expect(other.router.state.location.pathname).toBe('/transactions');
      expect(screen.getAllByText('PRIVATE MERCHANT 4321')).toHaveLength(rowsPerTab);

      serveAntiforgeryToken('retry-token');
      // The retry belongs to the notice (EXPERIENCE.md, Problem notice), so it sits inside the alert.
      await user.click(within(notice).getByRole('button', { name: 'Sign out' }));
      await waitFor(() => {
        expect(submitted).toHaveLength(1);
      });
      expect(submitted[0]?.token).toBe('retry-token');
      await tabs.flush();
      expect(tabs.messages.filter((message) => message !== 'activity')).toEqual(['signed-out']);
      tabs.close();
    },
  );

  it('shows the start page with no notice and tells the other tabs expired when the token fetch answers 401', async () => {
    failWith('/api/v1/antiforgery', 'unauthorized', 401);
    const submitted = recordFormSubmissions();
    const tabs = listenToTabs();
    const { user, router } = renderApp({ path: '/transactions' });
    await screen.findByText('PRIVATE MERCHANT 4321');
    await tabs.settle();

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/');
    });
    await tabs.flush();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(tabs.messages).toEqual(['expired']); // the other tabs learn the session is over
    expect(submitted).toHaveLength(0);
    tabs.close();
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
