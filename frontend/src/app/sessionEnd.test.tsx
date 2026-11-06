import { act, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import { getGetSessionQueryKey } from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  cachedCustomerData,
  failWith,
  makeRow,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

import { sessionEndedUrl } from './sessionEnd';

const sessionKey = getGetSessionQueryKey();
const rowText = 'ZXP*GLOBALSVCS 0800';

/** jsdom cannot navigate, so `location.replace` is a recorder; the page keeps what `pathname` and `search` say. */
function stubLocation(pathname = '/transactions', search = '') {
  const replace = vi.fn();
  vi.stubGlobal('location', { pathname, search, replace, reload: vi.fn() });
  return replace;
}

/** The path of every request the app makes from now on. */
function recordRequests() {
  const paths: string[] = [];
  server.events.on('request:start', ({ request }) => {
    paths.push(new URL(request.url).pathname);
  });
  return paths;
}

/** The tab is switched to: the browser fires `visibilitychange` on the document, then `focus` on the window. */
function bringTabToFront() {
  act(() => {
    document.dispatchEvent(new Event('visibilitychange', { bubbles: true }));
    window.dispatchEvent(new Event('focus'));
  });
}

beforeEach(() => {
  signInAs('linked');
  serveAntiforgeryToken();
  serveTransactions([makeRow()]);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the URL of the session-ended screen', () => {
  const guid = '0b9a1c7e-5d3f-4a62-9e1b-7c2d8f4a6b10';
  const longestPath = `/${'a'.repeat(2047)}`;

  it.each([
    ['a transactions list', '/transactions', '', '/transactions'],
    ['a raise route', '/transactions/TXN-123/dispute', '', '/transactions'],
    [
      'a raise review route in another case',
      '/Transactions/TXN-123/dispute/review',
      '',
      '/transactions',
    ],
    ['a dispute detail route', `/disputes/${guid}`, '', '/disputes'],
    [
      'a dispute detail route in another case, with a search',
      `/Disputes/${guid}`,
      '?tab=timeline',
      '/disputes',
    ],
    ['the disputes list', '/disputes', '', '/disputes'],
    ['another route, with a search', '/link/otp', '?step=2&x=a b', '/link/otp?step=2&x=a b'],
    ['a path of 2,048 characters', longestPath, '', longestPath],
    ['a path of 2,049 characters', `${longestPath}a`, '', '/'],
    ['a path and search of 2,049 characters', '/link', `?${'q'.repeat(2047)}`, '/'],
  ])('%s returns to the expected path', (_label, pathname, search, returnUrl) => {
    expect(sessionEndedUrl({ pathname, search })).toBe(
      `/signed-out?reason=expired&returnUrl=${encodeURIComponent(returnUrl)}`,
    );
  });
});

// Criterion 1: every way a session ends leaves the document, once, with nothing cleaned up in place.
describe('a session that ends in this tab', () => {
  async function openTransactions() {
    const app = renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    return app;
  }

  function expectLeftOnce(replace: ReturnType<typeof stubLocation>) {
    expect(replace).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith(
      `/signed-out?reason=expired&returnUrl=${encodeURIComponent('/transactions')}`,
    );
  }

  it('leaves the document when a query gets 401, and neither navigates in the SPA nor clears the cache', async () => {
    const replace = stubLocation();
    const { router, queryClient } = await openTransactions();
    failWith('/api/v1/disputes', 'unauthorized', 401);

    await act(() => router.navigate({ to: '/disputes' }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalled();
    });
    expectLeftOnce(replace);
    expect(router.state.location.pathname).toBe('/disputes');
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'linked' });
    expect(cachedCustomerData(queryClient)).not.toEqual([]);
  });

  it('leaves the document when a mutation gets 401', async () => {
    const replace = stubLocation();
    const { user } = await openTransactions();
    server.use(http.get('*/api/v1/antiforgery', () => new HttpResponse(null, { status: 401 })));

    await user.click(within(screen.getByRole('banner')).getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalled();
    });
    expectLeftOnce(replace);
  });

  it('leaves the document when a session refetch is answered anonymous', async () => {
    const replace = stubLocation();
    const { queryClient } = await openTransactions();
    signInAs('anonymous');

    await act(() => queryClient.refetchQueries({ queryKey: sessionKey }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalled();
    });
    expectLeftOnce(replace);
  });

  it('leaves once when a tab switch, its session answer and a 401 all end the session', async () => {
    const replace = stubLocation();
    const { router } = await openTransactions();
    signInAs('anonymous');
    failWith('/api/v1/disputes', 'unauthorized', 401);

    bringTabToFront();
    await act(() => router.navigate({ to: '/disputes' }));
    await waitFor(() => {
      expect(replace).toHaveBeenCalled();
    });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expectLeftOnce(replace);
  });

  it('does not leave when the app itself sets the session to unlinked, on a 403 linking-required', async () => {
    const replace = stubLocation();
    const { router, queryClient } = await openTransactions();
    failWith('/api/v1/disputes', 'linking-required', 403);

    await act(() => router.navigate({ to: '/disputes' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
    expect(queryClient.getQueryData(sessionKey)).toEqual({ state: 'unlinked' });
    expect(replace).not.toHaveBeenCalled();
  });

  it('does not leave when a session refetch is answered with a signed-in session', async () => {
    const replace = stubLocation();
    const { queryClient } = await openTransactions();
    signInAs('unlinked');

    await act(() => queryClient.refetchQueries({ queryKey: sessionKey }));

    expect(replace).not.toHaveBeenCalled();
  });
});

// Criterion 2: the session-ended screen is a fresh document that asks the API for nothing, so it never renews a session.
describe('the session-ended screen on a direct load', () => {
  it.each(['linked', 'anonymous'] as const)(
    'makes no request and shows the no-session header when the session cookie answers %s',
    async (state) => {
      signInAs(state);
      const requests = recordRequests();

      renderApp({ path: '/signed-out?reason=expired&returnUrl=%2Ftransactions' });

      await screen.findByRole('heading', { level: 1, name: "You've been signed out" });
      await act(async () => {
        await new Promise((resolve) => setTimeout(resolve, 50));
      });
      expect(requests).toEqual([]);
      expect(screen.getByRole('link', { name: 'DisputeDesk' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
      expect(screen.queryByRole('link', { name: 'Sign in' })).not.toBeInTheDocument();
      expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
    },
  );

  it('still asks for the session on another route', async () => {
    const requests = recordRequests();

    renderApp({ path: '/transactions' });

    await screen.findAllByText(rowText);
    expect(requests).toContain('/api/v1/authentication/session');
  });
});

// Criterion 4: another tab learns of the end when it is brought to the front.
describe('a tab brought to the front', () => {
  it('asks for the session once, and leaves the document when the answer is anonymous', async () => {
    const replace = stubLocation();
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const requests = recordRequests();
    signInAs('anonymous');

    bringTabToFront();

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
    expect(requests).toEqual(['/api/v1/authentication/session']);
  });

  it('asks for the session when an already visible window gains focus', async () => {
    const replace = stubLocation();
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const requests = recordRequests();
    signInAs('anonymous');

    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
    expect(requests).toEqual(['/api/v1/authentication/session']);
  });

  it('does not ask for the session when nobody is signed in', async () => {
    signInAs('anonymous');
    renderApp({ path: '/' });
    await screen.findByRole('heading', {
      level: 1,
      name: 'Something wrong with a card transaction?',
    });
    const requests = recordRequests();
    signInAs('linked');

    bringTabToFront();
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });

    expect(requests).toEqual([]);
    expect(
      screen.getByRole('heading', { level: 1, name: 'Something wrong with a card transaction?' }),
    ).toBeInTheDocument();
  });

  it('leaves the document on a 401 from the session request', async () => {
    const replace = stubLocation();
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    failWith('/api/v1/authentication/session', 'unauthorized', 401);

    bringTabToFront();

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
  });

  it('stays where it is when the session is live, and refetches no other query', async () => {
    const replace = stubLocation();
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const requests = recordRequests();

    bringTabToFront();

    await waitFor(() => {
      expect(requests).toEqual(['/api/v1/authentication/session']);
    });
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(requests).toEqual(['/api/v1/authentication/session']);
    expect(replace).not.toHaveBeenCalled();
  });
});

// Criterion 8: a page the browser restores from the back/forward cache still reloads.
describe('pageshow', () => {
  it('reloads the page when it was restored from the back/forward cache', async () => {
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const reload = vi.fn();
    vi.stubGlobal('location', { reload });

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does nothing on an ordinary page show', async () => {
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const reload = vi.fn();
    vi.stubGlobal('location', { reload });

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: false }));

    expect(reload).not.toHaveBeenCalled();
  });
});
