import { screen, waitFor, within } from '@testing-library/react';
import { HttpResponse } from 'msw';

import { problemResponse, makeRow, renderApp, serveTransactions, signInAs } from '@/test/support';
import { chooseReason, openRaiseScreen, recordRaiseRequests } from '@/test/raise';
import { appSourceFiles } from '@/test/sourceFiles';

const pathOf = (app: { router: { state: { location: { pathname: string } } } }) =>
  app.router.state.location.pathname;

// Criterion 1: the raise screens open only for a cached, disputable row.
describe('opening the raise screen', () => {
  it('opens from the Dispute control with a client-side navigation, and shows the row', async () => {
    const app = await openRaiseScreen([
      makeRow({
        description: 'ZXP*GLOBALSVCS 0800',
        transactionDate: '2026-10-26T09:14:00Z',
        maskedCard: '492345******3456',
        amount: { amount: '849.00', currency: 'ZAR' },
      }),
    ]);

    expect(pathOf(app)).toBe('/transactions/TXN-1/dispute');
    expect(screen.getByText('ZXP*GLOBALSVCS 0800')).toBeInTheDocument();
    expect(screen.getByText('Mon 26 Oct 2026')).toBeInTheDocument();
    expect(screen.getByText('Card 492345•• •••• 3456')).toBeInTheDocument();
    expect(screen.getByText('R849.00')).toBeInTheDocument();
  });

  it('redirects a deep link to /transactions, because no cached row has that transaction ID', async () => {
    signInAs('linked');
    serveTransactions([makeRow()]);
    const app = renderApp({ path: '/transactions/TXN-1/dispute' });

    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    expect(pathOf(app)).toBe('/transactions');
  });

  it('redirects a row that is not disputable to /transactions', async () => {
    const pending = makeRow({
      transactionId: 'TXN-2',
      disputable: false,
      notDisputableReason: 'notEligible',
      state: 'pending',
    });
    signInAs('linked');
    serveTransactions([pending]);
    const app = renderApp({ path: '/transactions' });
    await screen.findByText(pending.description);

    await app.router.navigate({ href: '/transactions/TXN-2/dispute' });

    await waitFor(() => {
      expect(pathOf(app)).toBe('/transactions');
    });
  });

  it('replaces the entry, so Back does not return to the redirected route', async () => {
    signInAs('linked');
    serveTransactions([makeRow()]);
    const app = renderApp({ path: '/transactions/TXN-1/dispute' });
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    expect(app.router.history.length).toBe(1);
  });

  it('sends a review with no draft to 4a', async () => {
    const app = await openRaiseScreen();

    await app.router.navigate({ href: '/transactions/TXN-1/dispute/review' });

    await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
    expect(pathOf(app)).toBe('/transactions/TXN-1/dispute');
  });
});

// Criterion 2: reason and expected amount.
describe('reason and expected amount', () => {
  it('shows the six reasons as a radio group with the copy deck label and explanation', async () => {
    await openRaiseScreen();

    const group = screen.getByRole('radiogroup', { name: 'Why are you disputing it?' });
    expect(screen.getAllByRole('radiogroup')).toHaveLength(1);
    expect(screen.queryByRole('group', { name: 'Why are you disputing it?' })).toBeNull();
    const labels = within(group)
      .getAllByRole('radio')
      .map((radio) => radio.getAttribute('aria-labelledby'))
      .map((id) => document.getElementById(id ?? '')?.textContent);
    expect(labels).toEqual([
      'Unrecognised',
      'Duplicate charge',
      'Wrong amount',
      'Not received',
      'Cancelled subscription',
      'Refund not processed',
    ]);
    expect(screen.getByText("I don't recognise this transaction.")).toBeInTheDocument();
    expect(screen.getByText("I was promised a refund that hasn't arrived.")).toBeInTheDocument();
  });

  it('shows Expected amount only for Wrong amount, and clears it when another reason is chosen', async () => {
    const { user } = await openRaiseScreen();
    expect(screen.queryByLabelText('Expected amount')).not.toBeInTheDocument();

    await chooseReason(user, 'Wrong amount');
    await user.type(screen.getByLabelText('Expected amount'), '80.00');
    await chooseReason(user, 'Not received');
    expect(screen.queryByLabelText('Expected amount')).not.toBeInTheDocument();

    await chooseReason(user, 'Wrong amount');
    expect(screen.getByLabelText('Expected amount')).toHaveValue('');
  });

  it('shows the amount field as a decimal text input with an R prefix', async () => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');

    const input = screen.getByLabelText('Expected amount');
    expect(input).toHaveAttribute('inputmode', 'decimal');
    expect(input).toHaveAttribute('type', 'text');
    expect(screen.getByText('R', { selector: 'span' })).toBeInTheDocument();
  });

  it('shows what the customer is disputing live: R849.00 less R80.00 is R769.00', async () => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');

    await user.type(screen.getByLabelText('Expected amount'), '80.00');

    expect(screen.getByText("You're disputing R769.00")).toBeInTheDocument();
  });

  it('shows no difference while the amount is not valid', async () => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');

    await user.type(screen.getByLabelText('Expected amount'), '80.001');

    expect(screen.queryByText(/You're disputing/)).not.toBeInTheDocument();
    // Every id the field points at exists.
    const ids = (screen.getByLabelText('Expected amount').getAttribute('aria-describedby') ?? '')
      .split(' ')
      .filter(Boolean);
    for (const id of ids) expect(document.getElementById(id)).not.toBeNull();
  });

  it.each([
    ['0.00', "You're disputing R849.00"],
    ['848.99', "You're disputing R0.01"],
  ])('accepts %s, the lowest and the highest amount allowed', async (amount, disputing) => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');
    await user.type(screen.getByLabelText('Expected amount'), amount);
    await user.tab();

    expect(screen.getByText(disputing)).toBeInTheDocument();
    expect(screen.getByLabelText('Expected amount')).not.toHaveAttribute('aria-invalid', 'true');
  });

  it.each([
    ['849.00', 'The expected amount must be less than R849.00.'],
    ['80.001', 'Enter an amount like 80.00, with at most 2 decimals.'],
  ])('refuses %s on blur', async (amount, message) => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');
    await user.type(screen.getByLabelText('Expected amount'), amount);
    await user.tab();

    expect(await screen.findByText(message)).toBeInTheDocument();
  });

  it('for any reason but Wrong amount the disputed amount is the transaction amount', async () => {
    const requests = recordRaiseRequests();
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Duplicate charge');
    await user.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByText("You're disputing")).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Submit dispute' }));
    await waitFor(() => {
      expect(requests).toHaveLength(1);
    });
    expect(requests[0]?.body.expectedAmount).toBeNull();
  });
});

// Criterion 3: the note and its character count.
describe('note with a character count', () => {
  const note = () => screen.getByLabelText('Note (optional)');
  const announcements = () => screen.getByRole('status');

  it('counts UTF-16 code units after trimming', async () => {
    const { user } = await openRaiseScreen();
    expect(screen.getByText('0 / 1,000')).toBeInTheDocument();

    await user.type(note(), '  hello 😀  ');

    expect(screen.getByText('8 / 1,000')).toBeInTheDocument();
  });

  it('announces politely at 900 and at 1,000 only', async () => {
    const { user } = await openRaiseScreen();

    await user.click(note());
    await user.paste('a'.repeat(899));
    expect(announcements()).toBeEmptyDOMElement();

    await user.paste('a');
    expect(announcements()).toHaveTextContent('100 characters left');

    await user.paste('a'.repeat(99));
    expect(announcements()).toHaveTextContent('100 characters left');
    await user.paste('a');
    expect(announcements()).toHaveTextContent("You've reached the limit of 1,000 characters");
  });

  it('keeps input past 1,000 and has no maxLength; it becomes the field error on Continue', async () => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Unrecognised');

    await user.click(note());
    await user.paste('a'.repeat(1001));

    expect(note()).not.toHaveAttribute('maxlength');
    expect(note()).toHaveValue('a'.repeat(1001));
    expect(screen.getByText('1001 / 1,000')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    expect(
      await screen.findAllByText('Your note must be 1,000 characters or fewer.'),
    ).not.toHaveLength(0);
    expect(
      screen.getByRole('heading', { level: 1, name: 'Dispute this transaction' }),
    ).toBeVisible();
  });
});

// Criterion 4: accessible field and form errors.
describe('field errors', () => {
  /** Asserts the control is invalid and that its `aria-describedby` names an element holding `message`. */
  function expectFieldError(control: HTMLElement, message: string) {
    expect(control).toHaveAttribute('aria-invalid', 'true');
    const ids = (control.getAttribute('aria-describedby') ?? '').split(' ');
    const texts = ids.map((id) => document.getElementById(id)?.textContent);
    expect(texts).toContain(message);
  }

  it('on Continue with no reason: the summary takes focus, links to the field, and the group is invalid', async () => {
    const { user } = await openRaiseScreen();

    await user.click(screen.getByRole('button', { name: 'Continue' }));

    const summary = await screen.findByRole('group', { name: 'Check the details below' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });
    expect(within(summary).getByRole('alert')).toHaveTextContent('Check the details below');
    expectFieldError(
      screen.getByRole('radiogroup'),
      "Choose why you're disputing this transaction.",
    );
    const link = within(summary).getByRole('link', {
      name: "Choose why you're disputing this transaction.",
    });
    await user.click(link);
    expect(screen.getAllByRole('radio')[0]).toHaveFocus();
  });

  it('takes focus on the summary even when a field error already shows after blur', async () => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Unrecognised');
    await user.click(screen.getByLabelText('Note (optional)'));
    await user.paste('a'.repeat(1001));
    await user.tab();
    expect(
      await screen.findAllByText('Your note must be 1,000 characters or fewer.'),
    ).not.toHaveLength(0);
    // React commits the summary's focus after React Hook Form's deferred refocus, so the summary
    // alone passes without shouldFocusError: false; the field must never be refocused at all.
    const noteFocused = vi.fn();
    screen.getByLabelText('Note (optional)').addEventListener('focus', noteFocused);

    await user.click(screen.getByRole('button', { name: 'Continue' }));

    const summary = await screen.findByRole('group', { name: 'Check the details below' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });
    expect(noteFocused).not.toHaveBeenCalled();
  });

  it('sets aria-invalid and an aria-describedby naming the error on the amount and the note too', async () => {
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Wrong amount');
    await user.click(screen.getByLabelText('Note (optional)'));
    await user.paste('a'.repeat(1001));

    await user.click(screen.getByRole('button', { name: 'Continue' }));

    const summary = await screen.findByRole('group', { name: 'Check the details below' });
    expectFieldError(
      screen.getByLabelText('Expected amount'),
      'Enter the amount you expected to pay.',
    );
    expectFieldError(
      screen.getByLabelText('Note (optional)'),
      'Your note must be 1,000 characters or fewer.',
    );
    expect(within(summary).getAllByRole('link')).toHaveLength(2);
  });

  it('shows no summary and no invalid field before the first Continue', async () => {
    await openRaiseScreen();

    expect(
      screen.queryByRole('group', { name: 'Check the details below' }),
    ).not.toBeInTheDocument();
    expect(screen.getByLabelText('Note (optional)')).not.toHaveAttribute('aria-invalid', 'true');
  });
});

// Criterion 4: a 400 on 4b returns to 4a.
describe('a 400 validation response to Submit', () => {
  async function submitWith400(errors: Record<string, string[]> | undefined) {
    recordRaiseRequests(() =>
      problemResponse('validation', 400, 'trace-1', errors ? { errors } : {}),
    );
    const app = await openRaiseScreen();
    await chooseReason(app.user, 'Wrong amount');
    await app.user.type(screen.getByLabelText('Expected amount'), '80.00');
    await app.user.click(screen.getByLabelText('Note (optional)'));
    await app.user.paste('never bought this');
    await app.user.click(screen.getByRole('button', { name: 'Continue' }));
    await app.user.click(await screen.findByRole('button', { name: 'Submit dispute' }));
    await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
    return app;
  }

  it('returns to 4a with the values kept and each named field set with its copy', async () => {
    await submitWith400({
      expectedAmount: ['The field ExpectedAmount is invalid.'],
      note: ['The note is too long.'],
    });

    expect(screen.getByLabelText('Expected amount')).toHaveValue('80.00');
    expect(screen.getByLabelText('Note (optional)')).toHaveValue('never bought this');
    expect(screen.getByRole('radio', { name: 'Wrong amount' })).toBeChecked();
    const summary = await screen.findByRole('group', { name: 'Check the details below' });
    await waitFor(() => {
      expect(summary).toHaveFocus();
    });
    expect(screen.getByLabelText('Expected amount')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByLabelText('Note (optional)')).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getAllByText('Your note must be 1,000 characters or fewer.')).not.toHaveLength(0);
    expect(screen.queryByText('The note is too long.')).not.toBeInTheDocument();
    expect(
      screen.getAllByText('Enter an amount less than R849.00, with at most 2 decimals.'),
    ).not.toHaveLength(0);
    expect(screen.queryByText('The field ExpectedAmount is invalid.')).not.toBeInTheDocument();
  });

  it('shows the form-level error for an unknown key', async () => {
    await submitWith400({ somethingElse: ['x'] });

    const summary = await screen.findByRole('group', { name: 'Check the details below' });
    expect(
      within(summary).getByText(
        "We couldn't check your dispute. Review the details and try again.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByLabelText('Expected amount')).not.toHaveAttribute('aria-invalid', 'true');
  });

  it('shows the form-level error for a 400 with no errors (a body binding failure)', async () => {
    await submitWith400(undefined);

    expect(
      await screen.findByText("We couldn't check your dispute. Review the details and try again."),
    ).toBeInTheDocument();
  });
});

// Boundaries: no problem body of a refused request is shown as it is.
describe('a body the screen does not know', () => {
  it('never shows a problem title or detail', async () => {
    recordRaiseRequests(() => HttpResponse.json({ title: 'Secret title' }, { status: 400 }));
    const { user } = await openRaiseScreen();
    await chooseReason(user, 'Unrecognised');
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await user.click(await screen.findByRole('button', { name: 'Submit dispute' }));

    await screen.findByRole('alert');
    expect(screen.queryByText('Secret title')).not.toBeInTheDocument();
  });
});

// Boundaries, CSP: the gateway sends `script-src 'self'; style-src 'self'`, so the app's markup has no inline script, no
// <style> element and no `style` attribute. (Radix sets a few styles through the CSSOM, which the policy allows.)
describe('content security policy', () => {
  it.each([
    ['a style attribute', /\bstyle=\{/],
    ['a style element', /<style\b/],
    ['an inline script', /<script\b/],
    ['raw HTML', /dangerouslySetInnerHTML/],
  ])('has no %s in the app source', (_name, pattern) => {
    const withoutComments = (text: string) =>
      text
        .split('\n')
        .filter((line) => !line.trim().startsWith('//'))
        .join('\n');
    const offenders = appSourceFiles(['.tsx']).filter(({ text }) =>
      pattern.test(withoutComments(text)),
    );

    expect(offenders.map(({ path }) => path)).toEqual([]);
  });
});
