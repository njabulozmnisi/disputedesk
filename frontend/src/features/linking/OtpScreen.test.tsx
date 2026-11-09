import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import { getGetSessionQueryKey } from '@/api/generated/disputedesk';
import { expectDecorativeIcon } from '@/test/icons';
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
const cells = () => Array.from(document.querySelectorAll('[data-slot="input-otp-slot"]'));
const cellDigits = () => cells().map((cell) => cell.textContent);
const hint =
  'The code expires 5 minutes after we send it. You can paste it, or let your phone fill it in.';
const sixDigitError = 'Enter the 6-digit code.';
// The elements that could echo the code: the alerts, the error summary and the field error. The cells hold the digits
// as text by design, so the whole body cannot be checked while the code is still in the field.
const echoingText = () =>
  [
    ...screen.queryAllByRole('alert'),
    screen.queryByRole('group', { name: 'Check the details below' }),
    document.getElementById('code-error'),
  ]
    .map((element) => element?.textContent ?? '')
    .join(' ');
// The field error only: a refused paste also puts the same words in the live region.
const fieldError = () => document.getElementById('code-error');
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

async function typeCode(user: UserEvent, code: string) {
  await user.clear(codeField());
  if (code) await user.type(codeField(), code);
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
  // Criterion 1: the step indicator sits above the h1; criterion 7: the help block ends the form.
  it('shows "Link your login, step 2 of 2" above the heading and ends with the help block', async () => {
    await openOtp();

    const step = screen.getByText('Link your login, step 2 of 2');
    const heading = screen.getByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(step.compareDocumentPosition(heading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const help = screen.getByRole('region', { name: 'Need help?' });
    expect(
      screen.getAllByRole('heading').map((entry) => [entry.tagName, entry.textContent]),
    ).toEqual([
      ['H1', 'Enter your OTP'],
      ['H2', 'Need help?'],
    ]);
    expect(help).toHaveTextContent("Didn't get a code? Start again.");
    expect(help).toHaveTextContent("Can't link online? Contact the bank.");
    expect(help.parentElement?.lastElementChild).toBe(help);
    expect(within(help).getByRole('link', { name: 'Start again' })).toHaveAttribute(
      'href',
      '/link',
    );
  });

  it('opens directly with the neutral message, nothing else from 2a, and no request', async () => {
    const start = recordLinkingRequests('start', () => noContent());
    const verifyRequests = recordLinkingRequests('verify', () => noContent());
    await openOtp();

    expect(document.title).toBe('Enter your OTP · DisputeDesk');
    expect(screen.getByText(neutral)).toBeInTheDocument();
    expect(screen.getByText(hint)).toBeInTheDocument();
    expect(screen.getByText("Can't link online? Contact the bank.")).toBeInTheDocument();
    expect(document.body).toHaveFocus();
    expect(start).toHaveLength(0);
    expect(verifyRequests).toHaveLength(0);
  });

  it('has one real input for the code with autofill, a numeric keypad and a limit of six digits', async () => {
    await openOtp();

    expect(codeField()).toHaveAttribute('id', 'code');
    expect(codeField()).toHaveAttribute('autocomplete', 'one-time-code');
    expect(codeField()).toHaveAttribute('inputmode', 'numeric');
    expect(codeField()).toHaveAttribute('spellcheck', 'false');
    expect(codeField()).toHaveAttribute('maxlength', '6');
    expect(codeField()).toHaveAttribute('pattern', '^\\d+$');
    expect(codeField()).toBeVisible();
    expect(screen.getAllByRole('textbox')).toHaveLength(1);
    expect(document.querySelectorAll('input[type="hidden"]')).toHaveLength(0);
  });

  // Criteria 3 and 4: six cells over the one input, hidden from assistive technology.
  it('draws six empty cells in a group that is hidden from assistive technology', async () => {
    await openOtp();

    expect(cells()).toHaveLength(6);
    expect(cellDigits()).toEqual(['', '', '', '', '', '']);
    for (const cell of cells()) expect(cell).not.toHaveAttribute('aria-invalid', 'true');
    expect(cells()[0]?.closest('[data-slot="input-otp-group"]')).toHaveAttribute(
      'aria-hidden',
      'true',
    );
    expect(codeField().closest('[aria-hidden="true"]')).toBeNull();
  });

  it('puts the typed digits in the cells, in order', async () => {
    const { user } = await openOtp();

    await user.type(codeField(), '481');

    expect(cellDigits()).toEqual(['4', '8', '1', '', '', '']);
  });

  it('describes the input with the help text under the cells, and adds the error when there is one', async () => {
    const { user } = await openOtp();

    expect(codeField()).toHaveAccessibleDescription(hint);
    expect(codeField()).toHaveAttribute('aria-describedby', 'code-hint');
    // The hint sits directly under the cells' container.
    expect(codeField().closest('[data-input-otp-container]')?.nextElementSibling).toBe(
      screen.getByText(hint),
    );

    await verify(user, '12345');

    expect(codeField()).toHaveAccessibleDescription(`${hint} ${sixDigitError}`);
    expect(codeField()).toHaveAttribute('aria-describedby', 'code-hint code-error');
  });

  it('does not set up the password-manager probe, which would call elementFromPoint', async () => {
    const elementFromPoint = vi.fn(() => null);
    Object.defineProperty(document, 'elementFromPoint', {
      value: elementFromPoint,
      configurable: true,
    });
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
    try {
      const { user } = await openOtp({ fakeTimers: true });
      await user.click(codeField());
      await passSeconds(6);

      expect(elementFromPoint).not.toHaveBeenCalled();
      expect(codeField().style.width).toBe('100%');
    } finally {
      Reflect.deleteProperty(document, 'elementFromPoint');
      vi.useRealTimers();
    }
  });

  it('marks the cells invalid together with the input after a refused submit', async () => {
    const { user } = await openOtp();

    await verify(user, '12345');

    expect(codeField()).toHaveAttribute('aria-invalid', 'true');
    for (const cell of cells()) expect(cell).toHaveAttribute('aria-invalid', 'true');
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
    'fills the six cells by pasting %j, with the spaces removed, and sends nothing',
    async (pasted) => {
      const requests = recordLinkingRequests('verify', () => noContent());
      const { user } = await openOtp();

      await enterCode(user, pasted);

      expect(codeField()).toHaveValue('481516');
      expect(cellDigits()).toEqual(['4', '8', '1', '5', '1', '6']);
      expect(requests).toHaveLength(0);
      expect(screen.queryByText(sixDigitError)).not.toBeInTheDocument();
    },
  );

  it('ignores a typed 7th digit and sends the first six', async () => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await typeCode(user, '4815167');

    expect(codeField()).toHaveValue('481516');
    await user.click(verifyButton());
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ code: '481516' });
  });

  it('drops a typed letter or space and keeps the digits', async () => {
    const { user } = await openOtp();

    await typeCode(user, '48 1a5');

    expect(codeField()).toHaveValue('4815');
  });

  // Criterion 3, the paste bound: 6 digits are accepted; more than 6 are refused whole, never truncated.
  it('refuses a pasted 7 digits into an empty field: the cells stay empty, the error shows, nothing is sent', async () => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await enterCode(user, '4815167');

    expect(codeField()).toHaveValue('');
    expect(cellDigits()).toEqual(['', '', '', '', '', '']);
    expect(fieldError()).toHaveTextContent(sixDigitError);
    expect(codeField()).toHaveAttribute('aria-invalid', 'true');
    expect(codeField()).toHaveAccessibleDescription(`${hint} ${sixDigitError}`);
    expect(screen.queryByRole('group', { name: 'Check the details below' })).toBeNull();
    expect(screen.getByRole('status')).toHaveTextContent(sixDigitError);
    expect(requests).toHaveLength(0);

    await user.click(verifyButton());

    expect(summary()).toHaveFocus();
    expect(requests).toHaveLength(0);
  });

  // Decision 2026-11-08: the bound is the digits in the pasted text, whatever else it holds.
  it.each(['4815167\n', '4815167\r\n', '481516 7', '4815167a'])(
    'refuses a paste of %j, which holds 7 digits: the cells stay empty, the error shows and is announced, nothing is sent',
    async (pasted) => {
      const requests = recordLinkingRequests('verify', () => noContent());
      const { user } = await openOtp();

      await enterCode(user, pasted);

      expect(codeField()).toHaveValue('');
      expect(cellDigits()).toEqual(['', '', '', '', '', '']);
      expect(fieldError()).toHaveTextContent(sixDigitError);
      expect(screen.getByRole('status')).toHaveTextContent(sixDigitError);
      expect(requests).toHaveLength(0);
    },
  );

  it.each(['481516\n', '481516a'])(
    'accepts a paste of %j, which holds 6 digits: the cells show 481516, no error, nothing announced',
    async (pasted) => {
      const requests = recordLinkingRequests('verify', () => noContent());
      const { user } = await openOtp();

      await enterCode(user, pasted);

      expect(codeField()).toHaveValue('481516');
      expect(cellDigits()).toEqual(['4', '8', '1', '5', '1', '6']);
      expect(fieldError()).toBeNull();
      expect(screen.getByRole('status')).toBeEmptyDOMElement();
      expect(requests).toHaveLength(0);
    },
  );

  // A paste error is the field's alone: the summary and its focus belong to Verify (EXPERIENCE.md, 2b).
  it('keeps focus in the field, and mounts no summary, when a 7-digit paste follows an earlier submit', async () => {
    recordLinkingRequests('verify', () => wrongCode(2));
    const { user } = await openOtp();
    await user.click(verifyButton());
    expect(summary()).toHaveFocus();
    await verify(user, '481516');
    await screen.findByText("That code didn't work. You have 2 tries left.");
    expect(screen.queryByRole('group', { name: 'Check the details below' })).toBeNull();

    await enterCode(user, '4815167');

    expect(codeField()).toHaveFocus();
    expect(screen.queryByRole('group', { name: 'Check the details below' })).toBeNull();
    expect(fieldError()).toHaveTextContent(sixDigitError);
    expect(screen.getByRole('status')).toHaveTextContent(sixDigitError);
  });

  it('keeps the summary it already shows, and its focus, when a 7-digit paste follows an invalid submit', async () => {
    const { user } = await openOtp();
    await user.click(verifyButton());
    expect(summary()).toHaveFocus();

    await user.click(codeField());
    await user.paste('4815167');

    expect(codeField()).toHaveFocus();
    expect(summary()).toBeInTheDocument();
    expect(fieldError()).toHaveTextContent(sixDigitError);
    expect(within(summary()).getByRole('link', { name: sixDigitError })).toBeInTheDocument();
  });

  it('refuses a pasted 7 digits with a space into a field holding 481: it keeps 481', async () => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();
    await user.type(codeField(), '481');

    await user.paste('4815 1678');

    expect(codeField()).toHaveValue('481');
    expect(cellDigits()).toEqual(['4', '8', '1', '', '', '']);
    expect(fieldError()).toHaveTextContent(sixDigitError);
    expect(codeField()).toHaveAttribute('aria-invalid', 'true');
    expect(requests).toHaveLength(0);

    await user.click(verifyButton());

    expect(summary()).toHaveFocus();
    expect(requests).toHaveLength(0);
  });

  it('sends a pasted 6 digits on Verify, with no error shown before', async () => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await enterCode(user, '481516');
    expect(screen.queryByText(sixDigitError)).not.toBeInTheDocument();
    await user.click(verifyButton());

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ code: '481516' });
  });

  it.each([
    ['48151a', 'a letter'],
    ['481-516', 'a hyphen'],
    ['Your code is 481516', 'words'],
    ['١٢٣٤٥٦', 'Arabic-Indic digits'],
  ])('ignores a paste of %j (%s) whole and leaves the field empty', async (pasted) => {
    const { user } = await openOtp();
    await user.click(codeField());

    await user.paste(pasted);

    expect(codeField()).toHaveValue('');
    expect(screen.queryByText(sixDigitError)).not.toBeInTheDocument();
  });

  it('replaces the last digit and truncates a paste into a full field', async () => {
    const { user } = await openOtp();
    await enterCode(user, '481516');
    // A browser selects the last digit of a full field when it gets focus (input-otp's focus listener).
    act(() => {
      codeField().blur();
      codeField().focus();
    });
    await user.paste('481516');

    expect(codeField()).toHaveValue('481514');
  });

  it('takes a programmatic fill, as autofill does, and sends nothing until Verify', async () => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    fireEvent.change(codeField(), { target: { value: '481516' } });

    expect(codeField()).toHaveValue('481516');
    expect(cellDigits()).toEqual(['4', '8', '1', '5', '1', '6']);
    expect(requests).toHaveLength(0);
    await user.click(verifyButton());
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ code: '481516' });
  });

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
    ['five digits', '48151', '48151'],
    ['a letter typed after five digits', '48151a', '48151'],
    ['empty', '', ''],
  ])('refuses %s with the summary focused, and sends nothing', async (_name, typed, held) => {
    const requests = recordLinkingRequests('verify', () => noContent());
    const { user } = await openOtp();

    await typeCode(user, typed);
    await user.click(verifyButton());

    expect(summary()).toHaveFocus();
    expect(screen.getAllByText('Enter the 6-digit code.')).toHaveLength(2);
    expect(codeField()).toHaveAttribute('aria-invalid', 'true');
    expect(codeField()).toHaveAccessibleDescription(`${hint} Enter the 6-digit code.`);
    expect(screen.getByRole('link', { name: 'Enter the 6-digit code.' })).toHaveAttribute(
      'href',
      '#code',
    );
    expect(codeField()).toHaveValue(held);
    expect(requests).toHaveLength(0);
    if (held) expect(echoingText()).not.toContain(held);
  });

  it('focuses the input from the summary link', async () => {
    const { user } = await openOtp();
    await verify(user, '12345');

    await user.click(screen.getByRole('link', { name: 'Enter the 6-digit code.' }));

    expect(codeField()).toHaveFocus();
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
    // A pending request is not a rate limit: the button stays enabled and keeps focus.
    expect(verifyButton()).toBeEnabled();
    expect(verifyButton()).toHaveFocus();
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
    expect(echoingText()).not.toContain('481516');
    for (const cell of cells()) expect(cell).toHaveAttribute('aria-invalid', 'true');
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
      expect(echoingText()).not.toContain('481516');
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
    expect(codeField()).toHaveAccessibleDescription(
      `${hint} That code didn't work. You have 2 tries left.`,
    );
    for (const cell of cells()) expect(cell).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText(neutral)).toBeInTheDocument();
    expect(await axe(document.body)).toHaveNoViolations();

    await verify(user, '222222');
    await screen.findByText("That code didn't work. You have 1 try left.");
    expect(codeField()).toHaveFocus();
    expect(echoingText()).not.toContain('111111');
    expect(echoingText()).not.toContain('222222');

    await verify(user, '333333');
    const heading = await screen.findByRole('heading', {
      level: 1,
      name: "We can't link you online",
    });
    expect(heading).toHaveFocus();
    expect(document.title).toBe("We can't link you online · DisputeDesk");
    // Criterion 5: the lock tile is inside the surface; the help line stays after it, outside.
    const surface = heading.closest('section');
    expectDecorativeIcon(surface?.querySelector('svg.lucide-lock'), 'size-6');
    expect(surface).toContainElement(screen.getByRole('link', { name: 'Start again' }));
    const help = screen.getByRole('region', { name: 'Need help?' });
    expect(surface).not.toContainElement(help);
    expect(surface?.nextElementSibling).toBe(help);
    expect(help).toHaveTextContent("Can't link online? Contact the bank.");
    expect(help).not.toHaveTextContent("Didn't get a code?");
    expect(
      screen.getAllByRole('heading').map((heading) => [heading.tagName, heading.textContent]),
    ).toEqual([
      ['H1', "We can't link you online"],
      ['H2', 'Need help?'],
    ]);
    expect(screen.queryByText(/step \d of 2/)).not.toBeInTheDocument();
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
    expect(queryClient.getQueryData(getGetSessionQueryKey())).toEqual({ state: 'linked' });
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
    expect(queryClient.getQueryData(getGetSessionQueryKey())).toEqual({ state: 'linked' });
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

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Too many attempts. Try again in 2 minutes.');
    expectDecorativeIcon(notice.querySelector('svg.lucide-clock'));
    expect(notice.querySelector('svg.lucide-triangle-alert')).not.toBeInTheDocument();
    expect(verifyButton()).toBeDisabled();
    expect(codeField()).toBeDisabled();
    expect(notice).toHaveFocus();
    expect(codeField()).toHaveValue('481516');
    expect(cellDigits()).toEqual(['4', '8', '1', '5', '1', '6']);
    expect(screen.getByText(neutral)).toBeInTheDocument();

    await passSeconds(119);
    await user.click(verifyButton());
    expect(requests).toHaveLength(1);

    await passSeconds(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(codeField()).toBeEnabled();
    expect(verifyButton()).toBeEnabled();
    await user.click(verifyButton());
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(requests).toHaveLength(2);
  });

  it('gives focus to Verify when the wait ends while the notice still has it', async () => {
    recordLinkingRequests('verify', () => rateLimited('2'));
    const { user } = await openOtp({ fakeTimers: true });
    await verify(user);
    expect(await screen.findByRole('alert')).toHaveFocus();

    await passSeconds(2);

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(verifyButton()).toBeEnabled();
    expect(verifyButton()).toHaveFocus();
  });

  it('moves focus to the notice after Enter in the field, as the focused input becomes disabled', async () => {
    recordLinkingRequests('verify', () => rateLimited('120'));
    const { user } = await openOtp({ fakeTimers: true });
    await enterCode(user, '481516');

    await user.keyboard('{Enter}');

    const notice = await screen.findByRole('alert');
    expect(codeField()).toBeDisabled();
    expect(notice).toHaveFocus();
  });

  it('leaves focus where the person moved it when the wait ends', async () => {
    recordLinkingRequests('verify', () => rateLimited('2'));
    const { user } = await openOtp({ fakeTimers: true });
    await verify(user);
    await screen.findByRole('alert');
    const startAgain = screen.getAllByRole('link', { name: 'Start again' })[0] as HTMLElement;
    act(() => {
      startAgain.focus();
    });

    await passSeconds(2);

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(startAgain).toHaveFocus();
    expect(verifyButton()).not.toHaveFocus();
  });

  it('keeps the help block, with "Start again", while rate limited', async () => {
    recordLinkingRequests('verify', () => rateLimited('900'));
    const { user } = await openOtp({ fakeTimers: true });
    await verify(user);
    await screen.findByRole('alert');

    const help = screen.getByRole('region', { name: 'Need help?' });
    expect(help).toHaveTextContent("Didn't get a code? Start again.");
    expect(help).toHaveTextContent("Can't link online? Contact the bank.");
    expect(screen.getAllByRole('heading').map((heading) => heading.tagName)).toEqual(['H1', 'H2']);
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
    expect(verifyButton()).toBeEnabled();
    expect(codeField()).toBeEnabled();
    expect(verifyButton()).toHaveFocus();
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
      expect(echoingText()).not.toContain('481516');
    },
  );
});
