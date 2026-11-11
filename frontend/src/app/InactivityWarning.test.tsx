import { act, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import { listTransactions } from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  cachedCustomerData,
  failWith,
  makeRow,
  problemResponse,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

const minute = 60 * 1000;
const warningTitle = "You'll be signed out soon.";

/** The warning comes 13 minutes after the last API response; these tests move the clock, never wait. */
async function passMinutes(minutes: number, seconds = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(minutes * minute + seconds * 1000);
  });
}

async function openTransactions() {
  const app = renderApp({ path: '/transactions', fakeTimers: true });
  await screen.findByRole('heading', { level: 1, name: 'Transactions' });
  return app;
}

// jsdom cannot navigate, so `location.replace` is a recorder; the page's path is `/transactions`.
let replace: ReturnType<typeof vi.fn>;

beforeEach(() => {
  // The timers and the wall clock are faked; the clock still ticks so promises and MSW keep moving.
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  replace = vi.fn();
  vi.stubGlobal('location', { pathname: '/transactions', search: '', replace });
  signInAs('linked');
  serveTransactions([makeRow()]);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

// Criterion 9: the warning before the idle sign-out.
describe('the inactivity warning', () => {
  it('appears after 13 minutes without an API response, with the EXPERIENCE.md copy', async () => {
    await openTransactions();

    await passMinutes(12, 59);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await passMinutes(0, 1);
    const dialog = await screen.findByRole('alertdialog', { name: warningTitle });
    expect(dialog).toHaveTextContent(
      'For your security, we sign you out after 15 minutes without activity.',
    );
    expect(screen.getByRole('button', { name: 'Stay signed in' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
  });

  it('draws the dialog title at the 24px h2 size, in the heading colour', async () => {
    await openTransactions();

    await passMinutes(13);
    const title = await screen.findByRole('heading', { name: warningTitle });
    expect(title).toHaveClass('text-h2', 'font-strong', 'text-heading');
  });

  it('has no accessibility violations while the warning is open', async () => {
    await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog', { name: warningTitle });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  // WAI-ARIA APG, Alert dialog: focus moves into the dialog when it opens, and Tab stays inside it (WCAG 2.2 SC 2.4.3).
  it('opens with focus on Stay signed in, and Tab and Shift+Tab stay inside the dialog', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog', { name: warningTitle });
    const stay = screen.getByRole('button', { name: 'Stay signed in' });
    const signOut = screen.getByRole('button', { name: 'Sign out' });

    expect(stay).toHaveFocus();
    await user.tab();
    expect(signOut).toHaveFocus();
    await user.tab();
    expect(stay).toHaveFocus();
    await user.tab({ shift: true });
    expect(signOut).toHaveFocus();
  });

  it('does not appear for someone who is not signed in', async () => {
    signInAs('anonymous');
    renderApp({ path: '/', fakeTimers: true });
    await screen.findByRole('heading', {
      level: 1,
      name: 'Something wrong with a card transaction?',
    });

    await passMinutes(20);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('restarts when an API response arrives', async () => {
    const { queryClient } = await openTransactions();
    await passMinutes(10);

    await act(() => queryClient.invalidateQueries());
    await passMinutes(12, 59);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await passMinutes(0, 1);
    expect(await screen.findByRole('alertdialog')).toBeInTheDocument();
  });

  it('restarts on an error response that is not a 401, because the server still renewed the session', async () => {
    const { queryClient } = await openTransactions();
    await passMinutes(10);
    failWith('/api/v1/transactions', 'core-banking-unavailable', 503);

    await act(() => queryClient.invalidateQueries());
    await passMinutes(12, 59);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('does not restart on a 401: the session had already ended', async () => {
    await openTransactions();
    await passMinutes(12);
    // A request outside the query cache, so only the timer sees the 401 and the person stays on the page.
    failWith('/api/v1/transactions', 'unauthorized', 401);
    await act(() => listTransactions().catch(() => undefined));

    await passMinutes(1);

    expect(await screen.findByRole('alertdialog', { name: warningTitle })).toBeInTheDocument();
  });

  it('leaves the document, once, when a 401 arrives during Load more', async () => {
    server.use(
      http.get('*/api/v1/transactions', ({ request }) => {
        if (new URL(request.url).searchParams.get('cursor') === null) {
          return HttpResponse.json({ items: [makeRow()], nextCursor: 'more' });
        }
        return HttpResponse.json(
          { title: 'unauthorized' },
          { status: 401, headers: { 'content-type': 'application/problem+json' } },
        );
      }),
    );
    const { user } = await openTransactions();
    await passMinutes(12);

    await user.click(await screen.findByRole('button', { name: 'Load more' }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
    expect(replace).toHaveBeenCalledWith(
      `/signed-out?reason=expired&returnUrl=${encodeURIComponent('/transactions')}`,
    );
  });

  it('treats Escape as Stay signed in: it renews the session, and the dialog closes only after that succeeds', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    let sessionRequests = 0;
    server.use(
      http.get('*/api/v1/authentication/session', () => {
        sessionRequests += 1;
        return HttpResponse.json({ state: 'linked' });
      }),
    );

    await user.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
    expect(sessionRequests).toBe(1);
  });

  it('keeps the dialog open after Escape when the renewal fails', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    failWith('/api/v1/authentication/session', 'core-banking-unavailable', 503);

    await user.keyboard('{Escape}');

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });
});

describe('Stay signed in', () => {
  it('asks the session endpoint, and closes the dialog only after that succeeds', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');

    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    let sessionRequests = 0;
    server.use(
      http.get('*/api/v1/authentication/session', async () => {
        sessionRequests += 1;
        await held;
        return HttpResponse.json({ state: 'linked' });
      }),
    );
    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    expect(sessionRequests).toBe(1);
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();

    release();
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
  });

  it('starts the 13 minutes again once the session is renewed', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await user.click(await screen.findByRole('button', { name: 'Stay signed in' }));
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });

    await passMinutes(12, 59);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await passMinutes(0, 1);
    expect(await screen.findByRole('alertdialog')).toBeInTheDocument();
  });

  it('leaves the document on a 401, with no renewal-failed message', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    failWith('/api/v1/authentication/session', 'unauthorized', 401);

    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
    expect(replace).toHaveBeenCalledWith(
      `/signed-out?reason=expired&returnUrl=${encodeURIComponent('/transactions')}`,
    );
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('leaves the document, once, when the server answers that nobody is signed in any more', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    signInAs('anonymous');

    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    await waitFor(() => {
      expect(replace).toHaveBeenCalledTimes(1);
    });
    expect(replace).toHaveBeenCalledWith(
      `/signed-out?reason=expired&returnUrl=${encodeURIComponent('/transactions')}`,
    );
  });

  it('asks the session endpoint once, even when it answers 503', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    let sessionRequests = 0;
    server.use(
      http.get('*/api/v1/authentication/session', () => {
        sessionRequests += 1;
        return problemResponse('core-banking-unavailable', 503);
      }),
    );

    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(sessionRequests).toBe(1);
  });

  it('keeps the dialog open and says so when the renewal fails, and works on the next try', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    failWith('/api/v1/authentication/session', 'core-banking-unavailable', 503);

    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    const dialog = await screen.findByRole('alertdialog');
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(dialog).toBeInTheDocument();

    signInAs('linked');
    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });
  });
});

describe('Sign out in the warning', () => {
  it('submits the sign-out form with a fresh token', async () => {
    serveAntiforgeryToken('fresh-token');
    const submitted: (string | null)[] = [];
    vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(function (
      this: HTMLFormElement,
    ) {
      submitted.push(new FormData(this).get('__RequestVerificationToken') as string | null);
    });
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(submitted).toEqual(['fresh-token']);
    });
  });

  it('keeps the screen and its data under the sign-out notice when the token cannot be fetched', async () => {
    failWith('/api/v1/antiforgery', 'core-banking-unavailable', 503);
    const { user, router, queryClient } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    // The dialog is modal, so the notice behind it is hidden from the accessibility tree: the failure shows inside it.
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent("We couldn't finish signing you out.");
    expect(screen.getByRole('alertdialog')).toContainElement(alert);
    expect(router.state.location.pathname).toBe('/transactions');
    expect(cachedCustomerData(queryClient)).not.toEqual([]);
  });

  it('shows no earlier sign-out failure when the warning opens again', async () => {
    failWith('/api/v1/antiforgery', 'core-banking-unavailable', 503);
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));
    await waitFor(() => {
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    });

    await passMinutes(13);

    const dialog = await screen.findByRole('alertdialog', { name: warningTitle });
    expect(within(dialog).queryByRole('alert')).not.toBeInTheDocument();
  });
});

// Criterion 3: the idle limit ends the session in this tab, as a wall-clock deadline.
describe('the idle limit in the browser', () => {
  function networkDown() {
    server.use(http.all('*', () => HttpResponse.error()));
    const requests: string[] = [];
    server.events.on('request:start', ({ request }) => {
      requests.push(new URL(request.url).pathname);
    });
    return requests;
  }

  function expectLeftOnce() {
    expect(replace).toHaveBeenCalledTimes(1);
    expect(replace).toHaveBeenCalledWith(
      `/signed-out?reason=expired&returnUrl=${encodeURIComponent('/transactions')}`,
    );
  }

  it('leaves the document when the timer fires at 15 minutes, without a request', async () => {
    await openTransactions();
    const requests = networkDown();

    await passMinutes(14, 59);
    expect(replace).not.toHaveBeenCalled();
    await passMinutes(0, 1);

    expectLeftOnce();
    expect(requests).toEqual([]);
  });

  it('leaves the document when the page becomes visible after the clock passed the deadline, though the timer never fired', async () => {
    await openTransactions();
    const requests = networkDown();

    vi.setSystemTime(Date.now() + 16 * minute); // asleep: the timer did not fire
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });

    expectLeftOnce();
    expect(requests).toEqual([]);
  });

  it('leaves the document when the window gains focus after the clock passed the deadline', async () => {
    await openTransactions();
    const requests = networkDown();

    vi.setSystemTime(Date.now() + 16 * minute);
    act(() => {
      window.dispatchEvent(new Event('focus'));
    });

    expectLeftOnce();
    expect(requests).toEqual([]);
  });

  it('stays while the clock is before the deadline when the page becomes visible', async () => {
    await openTransactions();

    vi.setSystemTime(Date.now() + 14 * minute);
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });

    expect(replace).not.toHaveBeenCalled();
  });

  it('moves the deadline when a tab brought to the front finds the session live', async () => {
    await openTransactions();
    await passMinutes(10);

    act(() => {
      document.dispatchEvent(new Event('visibilitychange', { bubbles: true }));
      window.dispatchEvent(new Event('focus'));
    });
    await passMinutes(14, 59); // 24:59 after the page loaded, 14:59 after the session answer
    expect(replace).not.toHaveBeenCalled();
    await passMinutes(0, 1);

    expectLeftOnce();
  });
});
