import { screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';

import { server } from '@/test/server';
import {
  failWith,
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
  serveTransactions();
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

    // A second sign-out (the first was cancelled by the browser, say) gets a new token, not the first one again.
    await user.click(screen.getByRole('button', { name: 'Sign out' }));
    await waitFor(() => {
      expect(submitted).toHaveLength(2);
    });
    expect(submitted[1]?.token).toBe('token-2');
  });

  it('shows the unexpected-error message and submits nothing when the token cannot be fetched', async () => {
    failWith('/api/v1/bff/antiforgery', 'core-banking-unavailable', 503);
    const submitted = recordFormSubmissions();
    const { user } = renderApp({ path: '/transactions' });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
    expect(submitted).toHaveLength(0);
  });

  it('takes the person to the session-ended screen when the session had already ended (401)', async () => {
    failWith('/api/v1/bff/antiforgery', 'unauthorized', 401);
    const submitted = recordFormSubmissions();
    const { user, router } = renderApp({ path: '/transactions' });

    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      expect(router.state.location.pathname).toBe('/signed-out');
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
