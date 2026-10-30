import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import type { SessionState } from '@/api/generated/model';
import { server } from '@/test/server';
import { failWith, renderApp, serveTransactions, signInAs } from '@/test/support';

const startHeading = 'Something wrong with a card transaction?';
const notFoundHeading = "We can't find that dispute";

beforeEach(() => {
  serveTransactions();
});

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

  it('sends an unlinked visitor of / to /link, which has no screen yet and so shows not-found', async () => {
    signInAs('unlinked');
    const { router } = renderApp({ path: '/' });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
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
    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
  });

  it('shows the transactions screen to a linked visitor', async () => {
    signInAs('linked');
    renderApp({ path: '/transactions' });

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Transactions' }),
    ).toBeInTheDocument();
  });

  it('sends a 403 linking-required from the API to /link', async () => {
    signInAs('linked');
    failWith('/api/v1/transactions', 'linking-required', 403);
    const { router } = renderApp({ path: '/transactions' });

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/link');
    });
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

// Criterion 10: screens whose story has not landed render not-found until it adds its route.
describe('screens not built yet', () => {
  it.each([
    '/link',
    '/link/otp',
    '/disputes',
    '/disputes/abc',
    '/transactions/TXN-1/dispute',
    '/transactions/TXN-1/dispute/review',
  ])('shows not-found at %s for a linked customer', async (path) => {
    signInAs('linked');
    renderApp({ path });

    expect(
      await screen.findByRole('heading', { level: 1, name: notFoundHeading }),
    ).toBeInTheDocument();
  });

  it('offers a way on from not-found', async () => {
    signInAs('linked');
    renderApp({ path: '/no/such/page' });

    expect(await screen.findByRole('link', { name: 'Go to your disputes' })).toHaveAttribute(
      'href',
      '/disputes',
    );
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
    renderApp({ path: '/transactions' });

    const heading = await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(document.title).toBe('Transactions · DisputeDesk');
    expect(heading).toHaveFocus();
  });
});
