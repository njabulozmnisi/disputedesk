import { act, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import { getApiV1Disputes, getGetSessionQueryKey } from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  failWith,
  cachedCustomerData,
  customerQueries,
  holdSessionEndedRoute,
  listenToTabs,
  makeRow,
  renderApp,
  serveAntiforgeryToken,
  serveDisputes,
  serveTransactions,
  signInAs,
} from '@/test/support';

import type { AppRouter } from './router';

const sessionKey = getGetSessionQueryKey();
const minute = 60 * 1000;
const rowText = 'ZXP*GLOBALSVCS 0800';

/** Two app instances in one page, as two tabs of one session: they share the channel by its name. */
async function openTwoTabs() {
  const first = renderApp({ path: '/transactions' });
  await screen.findAllByText(rowText);
  const rowsPerTab = screen.getAllByText(rowText).length;
  const second = renderApp({ path: '/transactions' });
  await waitFor(() => {
    expect(screen.getAllByText(rowText)).toHaveLength(2 * rowsPerTab);
  });
  await tabs.settle(); // loading the pages posted activity
  return { first, second, rowsPerTab };
}

/** Neither tab starts a request of its own when the session ends: only the test's probe and late request were made. */
function expectNoRequestOfTheirOwn(requests: string[]) {
  const own = requests.filter(
    (path) => !['/api/v1/disputes', '/api/v1/disputes/late'].includes(path),
  );
  expect(own).toEqual([]);
}

let tabs: ReturnType<typeof listenToTabs>;
beforeEach(() => {
  signInAs('linked');
  serveAntiforgeryToken();
  serveDisputes();
  serveTransactions([makeRow()]);
  tabs = listenToTabs();
});
afterEach(() => {
  tabs.close();
});

// Criterion 4: another tab clears when one tab's session ends.
describe('a tab receiving expired', () => {
  it('clears, shows the session-ended screen, posts nothing back, and a request in flight restores nothing', async () => {
    const { first, second } = await openTwoTabs();
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    server.use(
      http.get('*/api/v1/disputes/late', async () => {
        await held;
        return HttpResponse.json({});
      }),
    );
    const late = second.queryClient
      .query({ queryKey: ['late'], queryFn: () => fetch('/api/v1/disputes/late') })
      .catch(() => undefined);

    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });
    // The session-ended navigation stays pending, as a real one does, while the screens still mounted re-render.
    const open = holdSessionEndedRoute(second.router);
    failWith('/api/v1/disputes', 'unauthorized', 401);
    await act(() =>
      first.queryClient
        .query({ queryKey: ['probe'], queryFn: () => getApiV1Disputes() })
        .catch(() => undefined),
    );
    await tabs.flush();
    expectNoRequestOfTheirOwn(requests);
    open();

    await waitFor(() => {
      expect(second.router.state.location.pathname).toBe('/signed-out');
    });
    expect(cachedCustomerData(second.queryClient)).toEqual([]);
    release();
    await late;
    expect(cachedCustomerData(second.queryClient)).toEqual([]);
    await tabs.flush();
    expectNoRequestOfTheirOwn(requests);
    expect(tabs.messages).toEqual(['expired']); // the receiving tab never replies
  });
});

describe('a tab receiving signed-out', () => {
  it('clears and shows the start page, initiating no new request', async () => {
    const { second } = await openTwoTabs();
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });

    tabs.post('signed-out');

    await waitFor(() => {
      expect(second.router.state.location.pathname).toBe('/');
    });
    expect(cachedCustomerData(second.queryClient)).toEqual([]);
    expect(second.queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
    await tabs.flush();
    expect(requests).toEqual([]);
    expect(tabs.messages).toEqual(['signed-out']); // only the test's own post: the tab never replies
  });

  it('starts no request while the start page navigation is pending, and leaves no customer query after it', async () => {
    const { second } = await openTwoTabs();
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });
    // The navigation stays pending, as a real one does, while the transactions screen is still mounted.
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const navigate = second.router.navigate.bind(second.router);
    vi.spyOn(second.router, 'navigate').mockImplementation((async (
      ...args: Parameters<AppRouter['navigate']>
    ) => {
      await held;
      return navigate(...args);
    }) as AppRouter['navigate']);

    tabs.post('signed-out');
    await waitFor(() => {
      expect(cachedCustomerData(second.queryClient)).toEqual([]);
    });
    await tabs.flush();
    expect(requests).toEqual([]);

    release();
    await waitFor(() => {
      expect(second.router.state.location.pathname).toBe('/');
    });
    await tabs.flush();
    expect(requests).toEqual([]);
    expect(customerQueries(second.queryClient)).toEqual([]);
  });
});

describe('messages the tab does not act on', () => {
  it('ignores anything but the three strings', async () => {
    const { second, rowsPerTab } = await openTwoTabs();

    for (const message of ['EXPIRED', 'sign-out', '', { type: 'expired' }, null, 42]) {
      tabs.post(message);
    }
    await tabs.flush();

    expect(second.router.state.location.pathname).toBe('/transactions');
    expect(screen.getAllByText(rowText)).toHaveLength(2 * rowsPerTab);
  });

  it('ignores activity after the tab has ended: no session comes back and no timer starts', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    try {
      const { second } = await openTwoTabs();
      tabs.post('expired');
      await waitFor(() => {
        expect(second.router.state.location.pathname).toBe('/signed-out');
      });

      tabs.post('activity');
      await tabs.flush();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(20 * minute);
      });

      expect(second.queryClient.getQueryData(sessionKey)).toEqual({ state: 'anonymous' });
      expect(second.router.state.location.pathname).toBe('/signed-out');
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('the channel', () => {
  it('is closed when the app unmounts', async () => {
    const close = vi.spyOn(BroadcastChannel.prototype, 'close');
    const { first } = await openTwoTabs();
    close.mockClear();

    first.unmount();

    expect(close).toHaveBeenCalledTimes(1);
  });
});

// Criterion 5: a page restored from the back/forward cache reloads.
describe('pageshow', () => {
  function stubReload() {
    const reload = vi.fn();
    vi.stubGlobal('location', { reload });
    return reload;
  }
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reloads the page when it was restored from the back/forward cache', async () => {
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const reload = stubReload();

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does nothing on an ordinary page show', async () => {
    renderApp({ path: '/transactions' });
    await screen.findAllByText(rowText);
    const reload = stubReload();

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: false }));

    expect(reload).not.toHaveBeenCalled();
  });
});
