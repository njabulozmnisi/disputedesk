import { act, screen, waitFor } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { HttpResponse } from 'msw';
import { axe } from 'vitest-axe';

import { getGetApiV1BffSessionQueryKey } from '@/api/generated/disputedesk';
import { problemType } from '@/api/problems';
import {
  accepted,
  problemResponse,
  rateLimited,
  recordLinkingRequests,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

const validId = '8001015009087';
const validAccount = '1234567890';
const wrongCheckDigit = '8001015009088';

const idField = () => screen.getByLabelText('ID number');
const accountField = () => screen.getByLabelText('Account number');
const continueButton = () => screen.getByRole('button', { name: 'Continue' });
const summary = () => screen.getByRole('group', { name: 'Check the details below' });

async function openDetails(options: { fakeTimers?: boolean } = {}) {
  signInAs('unlinked');
  serveAntiforgeryToken();
  serveTransactions();
  const app = renderApp({ path: '/link', ...options });
  await screen.findByRole('heading', { level: 1, name: 'Link your login' });
  return app;
}

async function enter(user: UserEvent, id: string, account: string) {
  await user.clear(idField());
  await user.clear(accountField());
  if (id) {
    await user.click(idField());
    await user.paste(id);
  }
  if (account) {
    await user.click(accountField());
    await user.paste(account);
  }
}

async function submitDetails(user: UserEvent, id = validId, account = validAccount) {
  await enter(user, id, account);
  await user.click(continueButton());
}

async function passSeconds(seconds: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(seconds * 1000);
  });
}

// Criterion 2: the details form checks its bounds.
describe('screen 2a, the details form', () => {
  it('shows the title, two labelled inputs and Continue, with the inputs set up for a number', async () => {
    await openDetails();

    expect(document.title).toBe('Link your login · DisputeDesk');
    for (const input of [idField(), accountField()]) {
      expect(input).toHaveAttribute('inputmode', 'numeric');
      expect(input).toHaveAttribute('autocomplete', 'off');
      expect(input).toHaveAttribute('spellcheck', 'false');
    }
    expect(continueButton()).not.toHaveAttribute('aria-disabled', 'true');
    expect(screen.getByText("Can't link online? Contact the bank.")).toBeInTheDocument();
  });

  it('leaves focus alone on a direct load', async () => {
    await openDetails();

    expect(document.body).toHaveFocus();
  });

  it.each([
    ['an empty form', '', '', ['idNumber', 'accountNumber']],
    ['a 12-digit ID number', validId.slice(0, 12), validAccount, ['idNumber']],
    ['a 14-digit ID number', `${validId}0`, validAccount, ['idNumber']],
    ['a wrong check digit', wrongCheckDigit, validAccount, ['idNumber']],
    [
      'a space in the ID number',
      `${validId.slice(0, 6)} ${validId.slice(6)}`,
      validAccount,
      ['idNumber'],
    ],
    ['a letter in the ID number', `${validId.slice(0, 12)}a`, validAccount, ['idNumber']],
    ['a 5-digit account number', validId, '12345', ['accountNumber']],
    ['a 17-digit account number', validId, '12345678901234567', ['accountNumber']],
    ['a letter in the account number', validId, '12345a', ['accountNumber']],
    ['both wrong', wrongCheckDigit, '12345', ['idNumber', 'accountNumber']],
  ])('refuses %s, sends nothing, and focuses the summary', async (_name, id, account, broken) => {
    const requests = recordLinkingRequests('start', accepted);
    const { user } = await openDetails();

    await submitDetails(user, id, account);

    expect(summary()).toHaveFocus();
    const idMessage = 'Enter a valid 13-digit ID number.';
    const accountMessage = 'Enter a valid account number of 6 to 16 digits.';
    expect(screen.queryAllByText(idMessage)).toHaveLength(broken.includes('idNumber') ? 2 : 0);
    expect(screen.queryAllByText(accountMessage)).toHaveLength(
      broken.includes('accountNumber') ? 2 : 0,
    );
    expect(idField()).toHaveValue(id);
    expect(accountField()).toHaveValue(account);
    if (broken.includes('idNumber')) {
      expect(idField()).toHaveAttribute('aria-invalid', 'true');
      expect(idField()).toHaveAccessibleDescription(idMessage);
    }
    expect(requests).toHaveLength(0);
    // Nothing the customer typed is repeated in the copy.
    for (const typed of [id, account].filter(Boolean)) {
      expect(document.body.textContent).not.toContain(typed);
    }
  });

  it('points each summary link at a field id, and the link focuses the field', async () => {
    const { user } = await openDetails();
    await submitDetails(user, wrongCheckDigit, '12345');

    const links = screen.getAllByRole('link', { name: /Enter a valid/ });
    expect(links.map((link) => link.getAttribute('href'))).toEqual(['#idNumber', '#accountNumber']);
    await user.click(links[1] as HTMLElement);

    expect(accountField()).toHaveFocus();
  });

  it('takes the corrected values after an error: the summary goes and the next heading is focused', async () => {
    recordLinkingRequests('start', accepted);
    const { user } = await openDetails();
    await submitDetails(user, wrongCheckDigit, '12345');
    expect(summary()).toHaveFocus();

    await submitDetails(user);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(heading).toHaveFocus();
    expect(screen.queryByText('Check the details below')).not.toBeInTheDocument();
  });

  it('sends one start with the exact body and the antiforgery token for valid values', async () => {
    const requests = recordLinkingRequests('start', accepted);
    const { user } = await openDetails();

    await submitDetails(user);

    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body).toEqual({ idNumber: validId, accountNumber: validAccount });
    expect(Object.keys(requests[0]?.body as object)).toEqual(['idNumber', 'accountNumber']);
    expect(requests[0]?.headers.get('X-XSRF-TOKEN')).toBe('request-token');
  });

  it('blocks a second press while the first start is pending', async () => {
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => {
      release = resolve;
    });
    const requests = recordLinkingRequests('start', async () => {
      await held;
      return accepted();
    });
    const { user } = await openDetails();

    await enter(user, validId, validAccount);
    await user.click(continueButton());
    await waitFor(() => {
      expect(continueButton()).toHaveAttribute('aria-disabled', 'true');
    });
    expect(await axe(document.body)).toHaveNoViolations();
    await user.click(continueButton());
    await user.keyboard('{Enter}');
    release();

    await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(requests).toHaveLength(1);
  });

  it('keeps the focus in the field, not on the summary, when typing breaks it again after a failed submit', async () => {
    const { user } = await openDetails();
    await submitDetails(user, wrongCheckDigit, validAccount);
    expect(summary()).toHaveFocus();

    await user.click(idField());
    await user.keyboard('{Backspace}7');
    await user.type(idField(), '0');

    expect(idField()).toHaveFocus();
    expect(summary()).not.toHaveFocus();
  });

  it('has no accessibility violations empty and with field errors', async () => {
    const { user } = await openDetails();
    expect(await axe(document.body)).toHaveNoViolations();

    await submitDetails(user, wrongCheckDigit, '12345');
    expect(await axe(document.body)).toHaveNoViolations();
  });
});

// Criterion 2: every answer to a start.
describe('screen 2a, the answers to a start', () => {
  it.each([
    ['idNumber', 'Enter a valid 13-digit ID number.'],
    ['accountNumber', 'Enter a valid account number of 6 to 16 digits.'],
  ])('shows the message of a 400 naming %s, with the summary focused', async (key, message) => {
    recordLinkingRequests('start', () =>
      problemResponse('validation', 400, 'trace-1', { errors: { [key]: ['Detail from the API'] } }),
    );
    const { user } = await openDetails();

    await submitDetails(user);

    await waitFor(() => {
      expect(summary()).toHaveFocus();
    });
    expect(screen.getAllByText(message)).toHaveLength(2);
    expect(screen.queryByText('Detail from the API')).not.toBeInTheDocument();
    expect(idField()).toHaveValue(validId);
    expect(accountField()).toHaveValue(validAccount);
    expect(document.body.textContent).not.toContain(validId);
    expect(document.body.textContent).not.toContain(validAccount);
    expect(await axe(document.body)).toHaveNoViolations();
  });

  it.each([
    [
      'a 400 naming neither field',
      () => problemResponse('validation', 400, 'trace-1', { errors: { other: ['x'] } }),
    ],
    ['a 400 naming no field', () => problemResponse('validation', 400)],
    ['a 500', () => problemResponse('internal', 500)],
    ['a 404', () => problemResponse('not-found', 404)],
    ['a network failure', () => HttpResponse.error()],
  ])(
    'shows the problem notice for %s, keeps the values and leaves focus on Continue',
    async (_name, respond) => {
      recordLinkingRequests('start', respond);
      const { user } = await openDetails();

      await submitDetails(user);

      const notice = await screen.findByRole('alert');
      expect(notice).toHaveTextContent('Something went wrong on our side. Try again.');
      expect(screen.queryByText('internal')).not.toBeInTheDocument();
      expect(idField()).toHaveValue(validId);
      expect(accountField()).toHaveValue(validAccount);
      expect(continueButton()).toHaveFocus();
      expect(
        screen.queryByRole('group', { name: 'Check the details below' }),
      ).not.toBeInTheDocument();
      expect(document.body.textContent).not.toContain(validId);
      expect(document.body.textContent).not.toContain(validAccount);
      expect(await axe(document.body)).toHaveNoViolations();
    },
  );

  it('shows the trace id of a problem it cannot act on', async () => {
    recordLinkingRequests('start', () => problemResponse('internal', 500, 'trace-77'));
    const { user } = await openDetails();

    await submitDetails(user);

    expect(await screen.findByText('trace-77')).toBeInTheDocument();
  });

  it('sends again after a failure and moves on, so Continue is a working retry', async () => {
    const requests = recordLinkingRequests('start', (attempt) =>
      attempt === 1 ? problemResponse('internal', 500) : accepted(),
    );
    const { user, router } = await openDetails();
    await submitDetails(user);
    await screen.findByRole('alert');

    await user.click(continueButton());

    await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(requests).toHaveLength(2);
    expect(router.state.location.pathname).toBe('/link/otp');
  });

  it('goes to 2b and focuses its heading after a 202', async () => {
    recordLinkingRequests('start', accepted);
    const { user, router } = await openDetails();

    await submitDetails(user);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(router.state.location.pathname).toBe('/link/otp');
    expect(heading).toHaveFocus();
  });

  it('sends a login that is already linked to Transactions, with no announcement', async () => {
    recordLinkingRequests('start', () => problemResponse('already-linked', 403));
    const { user, router, queryClient } = await openDetails();

    await submitDetails(user);

    const heading = await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(heading).toHaveFocus();
    expect(router.state.location.pathname).toBe('/transactions');
    expect(queryClient.getQueryData(getGetApiV1BffSessionQueryKey())).toEqual({ state: 'linked' });
    expect(screen.getByRole('status')).toBeEmptyDOMElement();
  });
});

// Criterion 7: a rate-limited start.
describe('screen 2a, rate limited', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('says the wait in minutes, blocks the button for the exact seconds, then works again', async () => {
    const requests = recordLinkingRequests('start', (attempt) =>
      attempt === 1 ? rateLimited('120') : accepted(),
    );
    const { user } = await openDetails({ fakeTimers: true });

    await submitDetails(user);

    const notice = await screen.findByRole('alert');
    expect(notice).toHaveTextContent('Too many attempts. Try again in 2 minutes.');
    expect(continueButton()).toHaveAttribute('aria-disabled', 'true');
    expect(continueButton()).toHaveFocus();
    expect(idField()).not.toBeDisabled();
    expect(idField()).toHaveValue(validId);

    await user.click(continueButton());
    await passSeconds(119);
    await user.click(continueButton());
    expect(requests).toHaveLength(1);
    expect(screen.getByRole('alert')).toHaveTextContent('2 minutes');

    await passSeconds(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(continueButton()).not.toHaveAttribute('aria-disabled', 'true');
    await user.click(continueButton());
    await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(requests).toHaveLength(2);
  });

  it('reads an HTTP-date 12 minutes ahead', async () => {
    recordLinkingRequests('start', () =>
      rateLimited(new Date(Date.now() + 12 * 60 * 1000).toUTCString()),
    );
    const { user } = await openDetails({ fakeTimers: true });

    await submitDetails(user);

    expect(await screen.findByRole('alert')).toHaveTextContent('Try again in 12 minutes.');
  });

  it('says 1 minute for the gateway Retry-After of 1 second, and frees the button after that second', async () => {
    recordLinkingRequests('start', () => rateLimited('1'));
    const { user } = await openDetails({ fakeTimers: true });

    await submitDetails(user);

    expect(await screen.findByRole('alert')).toHaveTextContent('Try again in 1 minute.');
    expect(continueButton()).toHaveAttribute('aria-disabled', 'true');
    await passSeconds(1);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(continueButton()).not.toHaveAttribute('aria-disabled', 'true');
  });

  it.each([
    ['60', '1 minute.'],
    ['900', '15 minutes.'],
    ['61', '2 minutes.'],
    ['86400', '1440 minutes.'],
  ])('rounds a Retry-After of %s seconds up to %s', async (retryAfter, copy) => {
    recordLinkingRequests('start', () => rateLimited(retryAfter));
    const { user } = await openDetails({ fakeTimers: true });

    await submitDetails(user);

    expect(await screen.findByRole('alert')).toHaveTextContent(`Try again in ${copy}`);
  });

  it.each([
    ['no Retry-After (the full queue)', undefined],
    ['a Retry-After of 0', '0'],
    ['a Retry-After of 86401', '86401'],
    ['a date that is in the past', new Date(Date.now() - 60_000).toUTCString()],
    ['an unreadable Retry-After', 'soon'],
  ])('says "Try again later" and keeps Continue usable for %s', async (_name, retryAfter) => {
    const requests = recordLinkingRequests('start', (attempt) =>
      attempt === 1 ? rateLimited(retryAfter) : accepted(),
    );
    const { user } = await openDetails({ fakeTimers: true });

    await submitDetails(user);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Too many attempts. Try again later.',
    );
    expect(continueButton()).not.toHaveAttribute('aria-disabled', 'true');
    await user.click(continueButton());
    await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
    expect(requests).toHaveLength(2);
  });

  it.each([
    ['with a wait', '900'],
    ['without a wait', undefined],
  ])('has no accessibility violations while rate limited %s', async (_name, retryAfter) => {
    recordLinkingRequests('start', () => rateLimited(retryAfter));
    const { user } = await openDetails({ fakeTimers: true });
    await submitDetails(user);
    await screen.findByRole('alert');

    expect(await axe(document.body)).toHaveNoViolations();
  });

  it('does not repeat typed values in the 429 notice', async () => {
    recordLinkingRequests('start', () => rateLimited('900'));
    const { user } = await openDetails({ fakeTimers: true });

    await submitDetails(user);
    await screen.findByRole('alert');

    expect(document.body.textContent).not.toContain(validId);
    expect(document.body.textContent).not.toContain(validAccount);
  });
});

describe('screen 2a, problem type matching', () => {
  it('acts on the full type URI, not on the status alone', async () => {
    recordLinkingRequests('start', () =>
      HttpResponse.json(
        { type: `${problemType('rate-limited')}x`, status: 429, traceId: 't' },
        {
          status: 429,
          headers: { 'content-type': 'application/problem+json', 'Retry-After': '900' },
        },
      ),
    );
    const { user } = await openDetails();

    await submitDetails(user);

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Something went wrong on our side. Try again.',
    );
  });
});
