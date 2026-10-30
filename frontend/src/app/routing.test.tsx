import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import type { SessionState } from '@/api/generated/model';
import { server } from '@/test/server';
import {
  disputeGuid,
  failWith,
  makeDispute,
  problemResponse,
  renderApp,
  serveDisputes,
  serveTransactions,
  signInAs,
} from '@/test/support';

const startHeading = 'Something wrong with a card transaction?';
const notFoundHeading = "We can't find that dispute";
const linkHeading = 'Link your login';

beforeEach(() => {
  serveTransactions();
  serveDisputes();
});

/** Clicks a link and says whether the router took it over: a click it handles is `preventDefault`ed, so the browser
 * makes no document request. */
function clickIsHandledByRouter(link: HTMLElement): boolean {
  return !fireEvent.click(link);
}

// Criterion 2: routing by authorisation state.
describe('routing by authorisation state', () => {
  it('shows the start page to an anonymous visitor of /', async () => {
    signInAs('anonymous');
    const { router } = renderApp({ path: '/' });

    expect(
      await screen.findByRole('heading', { level: 1, name: startHeading }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/');
  });

  it('has no accessibility violations on the start page', async () => {
    signInAs('anonymous');
    renderApp({ path: '/' });
    await screen.findByRole('heading', { level: 1, name: startHeading });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('sends a linked visitor of / to /transactions', async () => {
    signInAs('linked');
    const { router } = renderApp({ path: '/' });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Transactions' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/transactions');
    expect(screen.queryByText(startHeading)).not.toBeInTheDocument();
  });

  it('sends an unlinked visitor of / to /link, which shows the details form', async () => {
    signInAs('unlinked');
    const { router } = renderApp({ path: '/' });

    expect(await screen.findByRole('heading', { level: 1, name: linkHeading })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/link');
  });

  it('sends an anonymous visitor of /transactions to the start page', async () => {
    signInAs('anonymous');
    const { router } = renderApp({ path: '/transactions' });

    expect(
      await screen.findByRole('heading', { level: 1, name: startHeading }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/');
  });

  it('sends an unlinked visitor of /transactions to /link', async () => {
    signInAs('unlinked');
    const { router } = renderApp({ path: '/transactions' });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
    expect(await screen.findByRole('heading', { level: 1, name: linkHeading })).toBeInTheDocument();
  });

  it('shows the transactions screen to a linked visitor', async () => {
    signInAs('linked');
    renderApp({ path: '/transactions' });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Transactions' }),
    ).toBeInTheDocument();
  });

  // The cache says linked; the 403 sets it to unlinked first, so /link shows 2a instead of bouncing back.
  it('sends a 403 linking-required from the API to /link, and stays there', async () => {
    signInAs('linked');
    let transactionRequests = 0;
    server.use(
      http.get('*/api/v1/transactions', () => {
        transactionRequests += 1;
        return problemResponse('linking-required', 403);
      }),
    );
    const { router } = renderApp({ path: '/transactions' });

    expect(await screen.findByRole('heading', { level: 1, name: linkHeading })).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(router.state.location.pathname).toBe('/link');
    expect(transactionRequests).toBe(1);
  });
});

describe('public routes render for any state', () => {
  const states: SessionState[] = ['anonymous', 'unlinked', 'linked'];

  it.each(states)(
    'shows the session-ended screen at /signed-out?reason=expired when %s',
    async (state) => {
      signInAs(state);
      const { router } = renderApp({ path: '/signed-out?reason=expired' });

      expect(
        await screen.findByRole('heading', { level: 1, name: "You've been signed out" }),
      ).toBeInTheDocument();
      expect(screen.getByText("Anything you hadn't submitted wasn't saved.")).toBeInTheDocument();
      expect(router.state.location.pathname).toBe('/signed-out');
    },
  );

  it('has no accessibility violations on the session-ended screen', async () => {
    signInAs('anonymous');
    renderApp({ path: '/signed-out?reason=expired' });
    await screen.findByRole('heading', { level: 1, name: "You've been signed out" });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('has no accessibility violations on the not-found screen', async () => {
    signInAs('linked');
    renderApp({ path: '/no/such/page' });
    await screen.findByRole('heading', { level: 1, name: notFoundHeading });

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it.each(states)('shows not-found for an unknown path when %s', async (state) => {
    signInAs(state);
    const { router } = renderApp({ path: '/no/such/page' });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/no/such/page');
  });

  // A real sign-out returns to /, so only the session-ended screen has copy at /signed-out.
  it.each(states)(
    'shows not-found, with no redirect, at /signed-out without a reason when %s',
    async (state) => {
      signInAs(state);
      const { router } = renderApp({ path: '/signed-out' });

      expect(
        await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
      ).toBeInTheDocument();
      expect(router.state.location.pathname).toBe('/signed-out');
    },
  );

  it.each(states)('shows not-found at /signed-out with another reason when %s', async (state) => {
    signInAs(state);
    const { router } = renderApp({ path: '/signed-out?reason=other' });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/signed-out');
  });

  // "Sign in again" returns to the same route; the BFF accepts only local paths (IsLocalUrl, AD-7).
  it.each(states)(
    'sends "Sign in again" back to the route the person was on when %s',
    async (state) => {
      signInAs(state);
      renderApp({ path: '/signed-out?reason=expired&returnUrl=%2Ftransactions%3Fx%3D1' });

      expect(await screen.findByRole('link', { name: 'Sign in again' })).toHaveAttribute(
        'href',
        '/api/v1/bff/login?returnUrl=%2Ftransactions%3Fx%3D1',
      );
    },
  );

  // The SPA keeps a value only when the browser's own URL parser (WHATWG) resolves it to this origin: the parser
  // drops tab and newline and reads "\\" as "/", so each of these would otherwise become another host.
  it.each([
    'https://evil.example/',
    '//evil.example/',
    '/\\evil.example/',
    'transactions',
    '',
    '/\t/evil.example',
    '/\n/evil.example',
    '/\r/evil.example',
    '/\t\\evil.example',
    '/\\\t/evil.example',
    '/\\',
    '/\t/',
    '/\\[',
  ])('never builds a login link from the non-local returnUrl %j', async (returnUrl) => {
    signInAs('anonymous');
    renderApp({ path: `/signed-out?reason=expired&returnUrl=${encodeURIComponent(returnUrl)}` });

    expect(await screen.findByRole('link', { name: 'Sign in again' })).toHaveAttribute(
      'href',
      '/api/v1/bff/login',
    );
  });

  it('never builds a login link from a raw %09 (tab) in the query string', async () => {
    signInAs('anonymous');
    renderApp({ path: '/signed-out?reason=expired&returnUrl=/%09/evil.example' });

    expect(await screen.findByRole('link', { name: 'Sign in again' })).toHaveAttribute(
      'href',
      '/api/v1/bff/login',
    );
  });

  it('does not build a login link from a returnUrl over the 2048 characters the BFF accepts', async () => {
    signInAs('anonymous');
    const tooLong = `/${'a'.repeat(2048)}`;
    renderApp({ path: `/signed-out?reason=expired&returnUrl=${tooLong}` });

    expect(await screen.findByRole('link', { name: 'Sign in again' })).toHaveAttribute(
      'href',
      '/api/v1/bff/login',
    );
  });

  it('links "Sign in again" to a plain login when no route was carried', async () => {
    signInAs('anonymous');
    renderApp({ path: '/signed-out?reason=expired' });

    expect(await screen.findByRole('link', { name: 'Sign in again' })).toHaveAttribute(
      'href',
      '/api/v1/bff/login',
    );
  });

  it('keeps showing the session-ended screen when the session request fails', async () => {
    failWith('/api/v1/bff/session', 'core-banking-unavailable', 503);
    renderApp({ path: '/signed-out?reason=expired' });

    expect(
      await screen.findByRole('heading', { level: 1, name: "You've been signed out" }),
    ).toBeInTheDocument();
  });
});

// Story 3.7: the Disputes screen, for a linked customer only.
describe('the disputes route', () => {
  it('shows the disputes screen to a linked visitor', async () => {
    signInAs('linked');
    renderApp({ path: '/disputes' });

    expect(await screen.findByRole('heading', { level: 1, name: 'Disputes' })).toBeInTheDocument();
  });

  it('sends an anonymous visitor of /disputes to the start page', async () => {
    signInAs('anonymous');
    const { router } = renderApp({ path: '/disputes' });

    expect(
      await screen.findByRole('heading', { level: 1, name: startHeading }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/');
  });

  it('sends an unlinked visitor of /disputes to /link', async () => {
    signInAs('unlinked');
    const { router } = renderApp({ path: '/disputes' });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
  });

  it('marks Disputes, and not Transactions, as the current page on /disputes', async () => {
    signInAs('linked');
    renderApp({ path: '/disputes' });

    const nav = await screen.findByRole('navigation', { name: 'Main' });
    expect(within(nav).getByRole('link', { name: 'Disputes' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(nav).getByRole('link', { name: 'Transactions' })).not.toHaveAttribute(
      'aria-current',
    );
    const tabs = screen.getByRole('navigation', { name: 'Main (phone)' });
    expect(within(tabs).getByRole('link', { name: /Disputes/ })).toHaveAttribute(
      'aria-current',
      'page',
    );
  });

  it.each(['Main', 'Main (phone)'])(
    'takes the %s navigation to Disputes without a document load',
    async (name) => {
      signInAs('linked');
      const { router } = renderApp({ path: '/transactions' });

      const link = within(await screen.findByRole('navigation', { name })).getByRole('link', {
        name: /Disputes/,
      });

      expect(clickIsHandledByRouter(link)).toBe(true);
      await waitFor(() => {
        expect(router.state.location.pathname).toBe('/disputes');
      });
    },
  );
});

// Story 6.4: the linking routes, for an unlinked login only.
describe('the linking routes', () => {
  it.each([
    ['/link', linkHeading],
    ['/link/otp', 'Enter your OTP'],
  ])('shows %s to an unlinked login', async (path, heading) => {
    signInAs('unlinked');
    const { router } = renderApp({ path });

    expect(await screen.findByRole('heading', { level: 1, name: heading })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe(path);
    expect(screen.queryByText(notFoundHeading)).not.toBeInTheDocument();
  });

  it.each(['/link', '/link/otp'])(
    'sends a linked customer from %s to /transactions before any linking request',
    async (path) => {
      signInAs('linked');
      const linkingRequests: string[] = [];
      server.events.on('request:start', ({ request }) => {
        if (new URL(request.url).pathname.startsWith('/api/v1/linking')) {
          linkingRequests.push(request.url);
        }
      });
      const { router } = renderApp({ path });

      expect(
        await screen.findByRole('heading', { level: 1, name: 'Transactions' }),
      ).toBeInTheDocument();
      expect(router.state.location.pathname).toBe('/transactions');
      expect(linkingRequests).toEqual([]);
    },
  );

  it.each(['/link', '/link/otp'])(
    'sends an anonymous visitor of %s to the start page',
    async (path) => {
      signInAs('anonymous');
      const { router } = renderApp({ path });

      expect(
        await screen.findByRole('heading', { level: 1, name: startHeading }),
      ).toBeInTheDocument();
      expect(router.state.location.pathname).toBe('/');
    },
  );

  it('shows only the wordmark and Sign out in the header, and the same help line on both screens', async () => {
    signInAs('unlinked');
    const { router } = renderApp({ path: '/link' });
    await screen.findByRole('heading', { level: 1, name: linkHeading });

    const onlyWordmarkAndSignOut = () => {
      expect(screen.getByRole('link', { name: 'DisputeDesk' })).toBeInTheDocument();
      expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
      expect(screen.getByText("Can't link online? Contact the bank.")).toBeInTheDocument();
    };
    onlyWordmarkAndSignOut();

    await router.navigate({ to: '/link/otp' });
    await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    onlyWordmarkAndSignOut();
  });
});

// Criterion 10: screens whose story has not landed render not-found until it adds its route.
describe('screens not built yet', () => {
  // The route exists (story 3.12), but `params.parse` refuses an ID that is not a GUID.
  it('shows not-found at /disputes/abc for a linked customer, through params.parse', async () => {
    signInAs('linked');
    renderApp({ path: '/disputes/abc' });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
  });

  it('offers a way on from not-found, without a document load', async () => {
    signInAs('linked');
    const { router } = renderApp({ path: '/no/such/page' });

    const link = await screen.findByRole('link', { name: 'Go to your disputes' });
    expect(link).toHaveAttribute('href', '/disputes');

    expect(clickIsHandledByRouter(link)).toBe(true);
    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/disputes');
    });
  });
});

// Criterion 2: a failed session request is an error, never the anonymous start page.
describe('when the session request fails', () => {
  it.each([
    [
      'a 503',
      () => {
        failWith('/api/v1/bff/session', 'core-banking-unavailable', 503);
      },
    ],
    [
      'a network error',
      () => {
        server.use(http.get('*/api/v1/bff/session', () => HttpResponse.error()));
      },
    ],
  ])('shows an error with Try again, not the start page, after %s', async (_name, breakSession) => {
    breakSession();
    const { user } = renderApp({ path: '/' });

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(screen.queryByText(startHeading)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();

    // Try again asks for the session again; this time it answers.
    signInAs('anonymous');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: startHeading }),
    ).toBeInTheDocument();
  });

  it('reads the session once more before it gives up on a 503', async () => {
    let requests = 0;
    server.use(
      http.get('*/api/v1/bff/session', () => {
        requests += 1;
        return HttpResponse.json(
          { title: 'x' },
          { status: 503, headers: { 'content-type': 'application/problem+json' } },
        );
      }),
    );
    renderApp({ path: '/transactions' });

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(requests).toBe(2);
  });

  it('shows the error on /transactions too', async () => {
    failWith('/api/v1/bff/session', 'core-banking-unavailable', 503);
    renderApp({ path: '/transactions' });

    expect(await screen.findByRole('button', { name: 'Try again' })).toBeInTheDocument();
    expect(screen.queryByText(startHeading)).not.toBeInTheDocument();
  });
});

describe('the shell', () => {
  it('shows the nav and Sign out to a linked customer', async () => {
    signInAs('linked');
    renderApp({ path: '/transactions' });

    const main = await screen.findByRole('navigation', { name: 'Main' });
    expect(main).toHaveTextContent('Transactions');
    expect(main).toHaveTextContent('Disputes');
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
  });

  it('shows only Sign out to an unlinked login: nothing else is reachable', async () => {
    signInAs('unlinked');
    renderApp({ path: '/no/such/page' });

    expect(await screen.findByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Main (phone)' })).not.toBeInTheDocument();
  });

  it('shows only the wordmark and a Sign in button on the start page', async () => {
    signInAs('anonymous');
    renderApp({ path: '/' });

    await screen.findByRole('heading', { level: 1, name: startHeading });
    expect(screen.queryByRole('navigation', { name: 'Main' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Sign out' })).not.toBeInTheDocument();
  });

  it('sets the document title and moves focus to the h1 when the route changes', async () => {
    signInAs('linked');
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(document.title).toBe('Transactions · DisputeDesk');

    await user.click(
      within(screen.getByRole('navigation', { name: 'Main' })).getByText('Disputes'),
    );

    const heading = await screen.findByRole('heading', { level: 1, name: 'Disputes' });
    expect(document.title).toBe('Disputes · DisputeDesk');
    await waitFor(() => {
      expect(heading).toHaveFocus();
    });
  });
});

// Story 3.7 criterion 6: the skip link.
describe('the skip link', () => {
  it('is the first thing Tab reaches on the first load, before the header', async () => {
    signInAs('linked');
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    await user.tab();

    const skipLink = screen.getByRole('link', { name: 'Skip to main content' });
    expect(skipLink).toHaveFocus();
    expect(screen.getByRole('banner').compareDocumentPosition(skipLink)).toBe(
      Node.DOCUMENT_POSITION_PRECEDING,
    );
  });

  it('is the first thing Tab reaches on the first load even when effects run twice (StrictMode)', async () => {
    signInAs('linked');
    const { user } = renderApp({ path: '/transactions', strictMode: true });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    await user.tab();

    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveFocus();
  });

  // Story 3.12: the detail screen shows loading, then the dispute, a failure or not-found on one `h1`, so a direct
  // load of its URL is still one screen and leaves focus alone.
  it.each([
    ['the dispute', 'ZXP*GLOBALSVCS 0800', () => HttpResponse.json(makeDispute())],
    ['a failure', 'Dispute', () => problemResponse('internal', 500)],
    ['an API 404', notFoundHeading, () => problemResponse('not-found', 404)],
  ])(
    'is the first thing Tab reaches on a direct load of a dispute that ends as %s, even when effects run twice (StrictMode)',
    async (_name, heading, respond) => {
      signInAs('linked');
      server.use(http.get(`*/api/v1/disputes/${disputeGuid}`, respond));
      const { user } = renderApp({ path: `/disputes/${disputeGuid}`, strictMode: true });
      await screen.findByRole('heading', { level: 1, name: heading });
      // The failure's heading reads "Dispute" while loading too, so wait for the notice.
      if (_name === 'a failure') {
        await screen.findByText('Something went wrong on our side. Try again.');
      }

      await user.tab();

      expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveFocus();
    },
  );

  it('is hidden until it has focus', async () => {
    signInAs('linked');
    renderApp({ path: '/transactions' });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    const skipLink = screen.getByRole('link', { name: 'Skip to main content' });
    expect(skipLink).toHaveClass('sr-only', 'focus:not-sr-only');
  });

  it.each(['Enter', 'click'])(
    'moves focus to main by script on %s, with no hash navigation',
    async (how) => {
      signInAs('linked');
      const { user, router } = renderApp({ path: '/transactions' });
      await screen.findByRole('heading', { level: 1, name: 'Transactions' });
      await user.tab();

      if (how === 'Enter') await user.keyboard('{Enter}');
      else await user.click(screen.getByRole('link', { name: 'Skip to main content' }));

      expect(screen.getByRole('main')).toHaveFocus();
      expect(screen.getByRole('main')).toHaveAttribute('id', 'main');
      expect(screen.getByRole('main')).toHaveAttribute('tabindex', '-1');
      expect(router.state.location.hash).toBe('');
      expect(router.state.location.pathname).toBe('/transactions');
    },
  );

  it('is on every screen, public ones too', async () => {
    signInAs('anonymous');
    renderApp({ path: '/' });
    await screen.findByRole('heading', { level: 1, name: startHeading });

    expect(screen.getByRole('link', { name: 'Skip to main content' })).toBeInTheDocument();
  });

  it('leaves focus on the h1, already past the header, after a route change', async () => {
    signInAs('linked');
    const { user } = renderApp({ path: '/transactions' });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    await user.click(
      within(screen.getByRole('navigation', { name: 'Main' })).getByText('Disputes'),
    );

    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 1, name: 'Disputes' })).toHaveFocus();
    });
    await user.tab();
    expect(screen.getByRole('link', { name: 'Skip to main content' })).not.toHaveFocus();
    expect(screen.getByRole('main').contains(document.activeElement)).toBe(true);
  });
});
