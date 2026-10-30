import { act, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import { getApiV1Transactions } from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  failWith,
  makeRow,
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

beforeEach(() => {
  // Only the timers are faked; the clock still ticks so promises and MSW keep moving.
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
  signInAs('linked');
  serveTransactions([makeRow()]);
});

afterEach(() => {
  vi.useRealTimers();
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

  it('has no accessibility violations while the warning is open', async () => {
    await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog', { name: warningTitle });

    expect(await axe(document.body)).toHaveNoViolations();
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
    await act(() => getApiV1Transactions().catch(() => undefined));

    await passMinutes(1);

    expect(await screen.findByRole('alertdialog', { name: warningTitle })).toBeInTheDocument();
  });

  it('ends at the session-ended screen, with no dialog, when a 401 arrives during Load more', async () => {
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
    const { user, router } = await openTransactions();
    await passMinutes(12);

    await user.click(await screen.findByRole('button', { name: 'Load more' }));
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    await passMinutes(5);

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 1, name: "You've been signed out" }),
    ).toBeInTheDocument();
  });

  it('treats Escape as Stay signed in: it renews the session, and the dialog closes only after that succeeds', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    let sessionRequests = 0;
    server.use(
      http.get('*/api/v1/bff/session', () => {
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
    failWith('/api/v1/bff/session', 'core-banking-unavailable', 503);

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
      http.get('*/api/v1/bff/session', async () => {
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

  it('goes to /signed-out?reason=expired on a 401', async () => {
    const { user, router } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    failWith('/api/v1/bff/session', 'unauthorized', 401);

    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    expect(router.state.location.search).toEqual({
      reason: 'expired',
      returnUrl: '/transactions',
    });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('goes to the session-ended screen when the server answers that nobody is signed in any more', async () => {
    const { user, router } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    signInAs('anonymous');

    await user.click(screen.getByRole('button', { name: 'Stay signed in' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
    });
    await passMinutes(0, 1); // the sign-in gate must not push the person on to the start page afterwards
    expect(router.state.location.pathname).toBe('/signed-out');
    expect(router.state.location.search).toEqual({
      reason: 'expired',
      returnUrl: '/transactions',
    });
  });

  it('keeps the dialog open and says so when the renewal fails, and works on the next try', async () => {
    const { user } = await openTransactions();
    await passMinutes(13);
    await screen.findByRole('alertdialog');
    failWith('/api/v1/bff/session', 'core-banking-unavailable', 503);

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

  it('shows the unexpected-error message in the dialog when the token cannot be fetched', async () => {
    failWith('/api/v1/bff/antiforgery', 'core-banking-unavailable', 503);
    const { user } = await openTransactions();
    await passMinutes(13);
    const dialog = await screen.findByRole('alertdialog');

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
  });
});
