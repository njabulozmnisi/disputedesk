import { act, screen, waitFor } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import { getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import { server } from '@/test/server';
import {
  noContent,
  problemResponse,
  rateLimited,
  recordLinkingRequests,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';
import { http } from 'msw';

const neutral =
  "If these details match our records, we've sent a code to the cellphone number we hold for you.";
const codeField = () => screen.getByLabelText('6-digit one-time code');
const verifyButton = () => screen.getByRole('button', { name: 'Verify' });
const summary = () => screen.getByRole('group', { name: 'Check the details below' });
const wrongCode = (attemptsRemaining?: unknown) =>
  problemResponse('otp-incorrect', 422, 'trace-1', { attemptsRemaining });

async function openOtp(options: { fakeTimers?: boolean } = {}) {
  signInAs('unlinked');
  serveAntiforgeryToken();
  serveTransactions();
  const app = renderApp({ path: '/link/otp', ...options });
  await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
  return app;
}

async function enterCode(user: UserEvent, code: string) {
  await user.clear(codeField());
  await user.click(codeField());
  await user.paste(code);
}

async function verify(user: UserEvent, code = '481516') {
  await enterCode(user, code);
  await user.click(verifyButton());
}

async function passSeconds(seconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(seconds * 1000);
  });
}

// Criteria 1, 3 and 4: screen 2b opens on its own and takes one pasteable code.
describe('screen 2b, the code form', () => {
  it('opens directly with the neutral message, nothing else from 2a, and no request', async () => {
    const start = recordLinkingRequests('start', () => noContent());
    const verifyRequests = recordLinkingRequests('verify', () => noContent());
    await openOtp();

    expect(document.title).toBe('Enter your OTP · DisputeDesk');
    expect(screen.getByText(neutral)).toBeInTheDocument();
    expect(screen.getByText('The code expires 5 minutes after we send it.')).toBeInTheDocument();
    expect(screen.getByText("Can't link online? Contact the bank.")).toBeInTheDocument();
    expect(document.body).toHaveFocus();
    expect(start).toHaveLength(0);
    expect(verifyRequests).toHaveLength(0);
  });

  it('has one plain input for the code with autofill, a numeric keypad and no length limit', async () => {
    await openOtp();

    expect(codeField()).toHaveAttribute('autocomplete', 'one-time-code');
    expect(codeField()).toHaveAttribute('inputmode', 'numeric');
    expect(codeField()).toHaveAttribute('spellcheck', 'false');
    expect(codeField()).not.toHaveAttribute('maxlength');
    expect(codeField()).toBeVisible();
    expect(screen.getAllByRole('textbox')).toHaveLength(1);
    expect(document.querySelectorAll('input[type="hidden"]')).toHaveLength(0);
  });

  it('links "Start again" to 2a with no request', async () => {
    const start = recordLinkingRequests('start', () => noContent());
    const { user, router } = await openOtp();

    await user.click(screen.getByRole('link', { name: 'Start again' }));

    const heading = await screen.findByRole('heading', { level: 1, name: 'Link your login' });
    expect(heading).toHaveFocus();
    expect(router.state.location.pathname).toBe('/link');
    expect(start).toHaveLength(0);
  });

  it.each(['481516', '481 516'])(
    'fills the one input by pasting %j and sends nothing',
    async (pasted) => {
      const requests = recordLinkingRequests('verify', () => noContent());
      const { user } = await openOtp();

      await enterCode(user, pasted);

      expect(codeField()).toHaveValue(pasted);
      expect(requests).toHaveLength(0);
    },
  );

  it('does not send when the sixth digit is typed, only when Verify is pressed', async () => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await user.type(codeField(), '481516');
    expect(requests).toHaveLength(0);
    await user.click(verifyButton());

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
  });

  it.each([
    ['481516', '481516'],
    ['481 516', '481516'],
    ['000123', '000123'],
  ])('sends %j as the string %j with the antiforgery token', async (typed, sent) => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await verify(user, typed);

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ code: sent });
    expect(requests[0]?.headers.get('X-XSRF-TOKEN')).toBe('request-token');
  });

  it.each([
    ['five digits', '48151'],
    ['seven digits', '4815167'],
    ['a letter', '48151a'],
    ['empty', ''],
  ])('refuses %s with the summary focused, and sends nothing', async (_name, code) => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await verify(user, code);

    expect(summary()).toHaveFocus();
    expect(screen.getAllByText('Enter the 6-digit code.')).toHaveLength(2);
    expect(codeField()).toHaveAttribute('aria-invalid', 'true');
    expect(codeField()).toHaveAccessibleDescription('Enter the 6-digit code.');
    expect(screen.getByRole('link', { name: 'Enter the 6-digit code.' })).toHaveAttribute(
      'href',
      '#code',
    );
    expect(codeField()).toHaveValue(code);
    expect(requests).toHaveLength(0);
    if (code) expect(document.body.textContent).not.toContain(code);
  });

  it('blocks a second press while the first verify is pending', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const requests = recordLinkingRequests('verify', async () => {
      await held;
      return noContent();
    });
    const { user } = await openOtp();

    await enterCode(user, '481516');
    await user.click(verifyButton());
    await waitFor(() => {
      expect(verifyButton()).toHaveAttribute('aria-disabled', 'true');
    });
    expect(await axe(document.body)).toHaveNoViolations();
    await user.click(verifyButton());
    await user.keyboard('{Enter}');
    release();

    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(requests).toHaveLength(1);
  });

  it('shows the message of a 400 naming code, with the summary focused', async () => {
    recordLinkingRequests('verify', () =>
      problemResponse('validation', 400, 'trace-1', { errors: { code: ['API text'] } }),
    );
    const { user } = await openOtp();

    await verify(user);

    await waitFor(() => {
      expect(summary()).toHaveFocus();
    });
    expect(screen.getAllByText('Enter the 6-digit code.')).toHaveLength(2);
    expect(screen.queryByText('API text')).not.toBeInTheDocument();
    expect(document.body.textContent).not.toContain('481516');
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it.each([
    [
      'a 400 naming another field',
      () => problemResponse('validation', 400, 'trace-1', { errors: { x: ['y'] } }),
    ],
    ['a 500', () => problemResponse('internal', 500)],
    ['a 404', () => problemResponse('not-found', 404)],
    ['a network failure', () => HttpResponse.error()],
  ])(
    'shows the problem notice for %s, keeps the code and leaves focus on Verify',
    async (_name, respond) => {
      recordLinkingRequests('verify', respond);
      const { user } = await openOtp();

      await verify(user);

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Something went wrong on our side. Try again.',
      );
      expect(codeField()).toHaveValue('481516');
      expect(verifyButton()).toHaveFocus();
      expect(document.body.textContent).not.toContain('481516');
      expect(await axe(document.body)).toHaveNoViolations();
    },
  );

  it('has no accessibility violations empty and with a field error', async () => {
    const { user } = await openOtp();
    expect(await axe(document.body)).toHaveNoViolations();

    await verify(user, '12345');
    expect(await axe(document.body)).toHaveNoViolations();
  });
});

// Criterion 5: a wrong code and a lockout.
describe('screen 2b, wrong codes and the lockout', () => {
  it('keeps the focus in the input, not on the summary, when typing breaks it again after a failed submit', async () => {
    const { user } = await openOtp();
    await verify(user, '48151');
    expect(summary()).toHaveFocus();

    await user.type(codeField(), '6');
    await user.type(codeField(), '7');

    expect(codeField()).toHaveFocus();
    expect(summary()).not.toHaveFocus();
  });

  it('keeps the focus in the input while typing after a 422, and the alert still gives the tries left', async () => {
    recordLinkingRequests('verify', () => wrongCode(2));
    const { user } = await openOtp();
    await verify(user, '111111');
    await screen.findByText("That code didn't work. You have 2 tries left.");

    await user.type(codeField(), '4');

    expect(codeField()).toHaveFocus();
    expect(screen.getByRole('alert')).toHaveTextContent('You have 2 tries left.');
  });

  it('shows the tries left, empties the input and focuses it, then locks after the third', async () => {
    recordLinkingRequests('verify', (attempt) => {
      if (attempt === 1) return wrongCode(2);
      if (attempt === 2) return wrongCode(1);
      return problemResponse('linking-locked', 409);
    });
    const { user } = await openOtp();

    await verify(user, '111111');
    const first = await screen.findByRole('alert');
    expect(first).toHaveTextContent("That code didn't work. You have 2 tries left.");
    expect(codeField()).toHaveValue('');
    expect(codeField()).toHaveFocus();
    expect(codeField()).toHaveAccessibleDescription(/You have 2 tries left/);
    expect(screen.getByText(neutral)).toBeInTheDocument();
    expect(await axe(document.body)).toHaveNoViolations();

    await verify(user, '222222');
    await screen.findByText("That code didn't work. You have 1 try left.");
    expect(codeField()).toHaveFocus();
    expect(document.body.textContent).not.toContain('111111');
    expect(document.body.textContent).not.toContain('222222');

    await verify(user, '333333');
    const heading = await screen.findByRole('heading', {
      level: 1,
      name: "We can't link you online",
    });
    expect(heading).toHaveFocus();
    expect(document.title).toBe("We can't link you online · DisputeDesk");
    expect(screen.getByText('Please contact the bank.')).toBeInTheDocument();
    expect(
      screen.getByText('If you think you mistyped your details or the code, you can start again.'),
    ).toBeInTheDocument();
    expect(screen.queryByLabelText('6-digit one-time code')).not.toBeInTheDocument();
    expect(screen.queryByText(/tries? left/)).not.toBeInTheDocument();
    expect(screen.queryByText(neutral)).not.toBeInTheDocument();
    expect(screen.getByText("Can't link online? Contact the bank.")).toBeInTheDocument();
    expect(document.body.textContent).not.toContain('333333');
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('offers "Start again" from the locked state, which goes to 2a with no request', async () => {
    recordLinkingRequests('verify', () => problemResponse('linking-locked', 409));
    const start = recordLinkingRequests('start', () => noContent());
    const { user, router } = await openOtp();
    await verify(user);
    await screen.findByRole('heading', { level: 1, name: "We can't link you online" });

    await user.click(screen.getByRole('link', { name: 'Start again' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Link your login' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/link');
    expect(start).toHaveLength(0);
  });

  it.each([
    ['missing', undefined],
    ['0', 0],
    ['3', 3],
    ['1.5', 1.5],
    ['a string', '2'],
    ['null', null],
  ])('shows no count when attemptsRemaining is %s', async (_name, attemptsRemaining) => {
    recordLinkingRequests('verify', () => wrongCode(attemptsRemaining));
    const { user } = await openOtp();

    await verify(user);

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/^That code didn't work\.$/);
    expect(codeField()).toHaveFocus();
  });

  it('reads attemptsRemaining only from otp-incorrect', async () => {
    recordLinkingRequests('verify', () =>
      problemResponse('validation', 400, 'trace-1', { attemptsRemaining: 2, errors: { x: ['y'] } }),
    );
    const { user } = await openOtp();

    await verify(user);

    await screen.findByRole('alert');
    expect(screen.queryByText(/tries? left/)).not.toBeInTheDocument();
  });

  it('shows the same copy for a login with no challenge, every time', async () => {
    recordLinkingRequests('verify', () => wrongCode(2));
    const { user } = await openOtp();

    await verify(user);
    await screen.findByText("That code didn't work. You have 2 tries left.");
    await verify(user);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(
        "That code didn't work. You have 2 tries left.",
      );
    });
  });

  it('links on a correct third code after two wrong ones', async () => {
    const requests = recordLinkingRequests('verify', (attempt) => {
      if (attempt === 1) return wrongCode(2);
      if (attempt === 2) return wrongCode(1);
      return noContent();
    });
    const { user, router } = await openOtp();

    await verify(user, '111111');
    await screen.findByText(/You have 2 tries left/);
    await verify(user, '222222');
    await screen.findByText(/You have 1 try left/);
    await verify(user, '481516');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Transactions' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/transactions');
    expect(requests).toHaveLength(3);
  });
});

// Criterion 6: a link lands on Transactions and stays there.
describe('screen 2b, a link', () => {
  it('sets the session to linked, goes to Transactions, requests them and announces the link', async () => {
    recordLinkingRequests('verify', () => noContent());
    const { user, router, queryClient } = await openOtp();
    const transactionRequests: string[] = [];
    server.use(
      http.get('*/api/v1/transactions', () => {
        transactionRequests.push('GET');
        return HttpResponse.json({ items: [], nextCursor: null });
      }),
    );

    await verify(user);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(heading).toHaveFocus();
    expect(router.state.location.pathname).toBe('/transactions');
    expect(queryClient.getQueryData(getGetApiV1BffSessionQueryKey())).toEqual({ state: 'linked' });
    expect(transactionRequests).toHaveLength(1);
    expect(screen.getByRole('status')).toHaveTextContent('Your login is linked.');
    expect(router.state.location.pathname).toBe('/transactions');
  });

  it('treats a 403 already-linked the same way, without an announcement', async () => {
    recordLinkingRequests('verify', () => problemResponse('already-linked', 403));
    const { user, router, queryClient } = await openOtp();

    await verify(user);

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Transactions' }),
    ).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/transactions');
    expect(queryClient.getQueryData(getGetApiV1BffSessionQueryKey())).toEqual({ state: 'linked' });
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });
});

// Criterion 7: a rate-limited verify.
describe('screen 2b, rate limited', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('says the wait, keeps the code, blocks Verify for the exact seconds, then works again', async () => {
    const requests = recordLinkingRequests('verify', (attempt) =>
      attempt === 1 ? rateLimited('120') : noContent(),
    );
    const { user } = await openOtp({ fakeTimers: true });

    await verify(user);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Too many attempts. Try again in 2 minutes.',
    );
    expect(verifyButton()).toHaveAttribute('aria-disabled', 'true');
    expect(verifyButton()).toHaveFocus();
    expect(codeField()).toHaveValue('481516');
    expect(screen.getByText(neutral)).toBeInTheDocument();

    await passSeconds(119);
    await user.click(verifyButton());
    expect(requests).toHaveLength(1);

    await passSeconds(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    await user.click(verifyButton());
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(requests).toHaveLength(2);
  });

  it('says "Try again later" and keeps Verify usable when there is no Retry-After', async () => {
    const requests = recordLinkingRequests('verify', (attempt) =>
      attempt === 1 ? rateLimited() : noContent(),
    );
    const { user } = await openOtp({ fakeTimers: true });

    await verify(user);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Too many attempts. Try again later.',
    );
    expect(verifyButton()).not.toHaveAttribute('aria-disabled', 'true');
    await user.click(verifyButton());
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(requests).toHaveLength(2);
  });

  it.each([
    ['with a wait', '900'],
    ['without a wait', undefined],
  ])(
    'has no accessibility violations rate limited %s, and shows no typed code',
    async (_n, wait) => {
      recordLinkingRequests('verify', () => rateLimited(wait));
      const { user } = await openOtp({ fakeTimers: true });
      await verify(user);
      await screen.findByRole('alert');

      expect(await axe(document.body)).toHaveNoViolations();
      expect(document.body.textContent).not.toContain('481516');
    },
  );
});
