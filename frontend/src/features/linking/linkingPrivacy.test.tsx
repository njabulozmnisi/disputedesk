import { screen, waitFor } from '@testing-library/react';

import { flushSpans, endedSpans } from '@/test/spans';
import {
  accepted,
  noContent,
  recordLinkingRequests,
  renderApp,
  serveAntiforgeryToken,
  serveTransactions,
  signInAs,
} from '@/test/support';

const idNumber = '8001015009087';
const accountNumber = '1234567890';
const code = '481516';

// Criterion 1: no ID number, account number or code is kept anywhere (ASVS 5.0 V14.3, V16.2).
describe('what the linking flow leaves behind', () => {
  // The memory history keeps its entries private, so walk back to the first and forward through each one.
  async function everyHistoryEntry(router: Awaited<ReturnType<typeof runTheFlow>>['router']) {
    const last = router.history.length - 1;
    const entries: unknown[] = [];
    router.history.go(-last);
    for (let index = 0; index <= last; index += 1) {
      await waitFor(() => {
        expect(router.history.location.state.__TSR_index).toBe(index);
      });
      const { href, hash, state } = router.history.location;
      entries.push({ href, hash, state });
      if (index < last) router.history.forward();
    }
    expect(entries).toHaveLength(last + 1);
    return entries;
  }

  async function runTheFlow() {
    signInAs('unlinked');
    serveAntiforgeryToken();
    serveTransactions();
    recordLinkingRequests('start', accepted);
    recordLinkingRequests('verify', noContent);
    const app = renderApp({ path: '/link' });
    await screen.findByRole('heading', { level: 1, name: 'Link your login' });
    return app;
  }

  async function startFlow(app: Awaited<ReturnType<typeof runTheFlow>>) {
    const { user } = app;
    await user.click(screen.getByLabelText('ID number'));
    await user.paste(idNumber);
    await user.click(screen.getByLabelText('Account number'));
    await user.paste(accountNumber);
    await user.click(screen.getByRole('button', { name: 'Continue' }));
    await screen.findByRole('heading', { level: 1, name: 'Enter your OTP' });
  }

  const mutationCache = (app: Awaited<ReturnType<typeof runTheFlow>>) =>
    JSON.stringify(
      app.queryClient
        .getMutationCache()
        .getAll()
        .map((mutation) => mutation.state),
    );

  it('keeps the mutation cache empty of submitted values after the 202 and after the 204', async () => {
    const app = await runTheFlow();

    await startFlow(app);
    await waitFor(() => {
      expect(app.queryClient.getMutationCache().getAll()).toHaveLength(0);
    });
    expect(mutationCache(app)).not.toContain(idNumber);
    expect(mutationCache(app)).not.toContain(accountNumber);

    await app.user.click(screen.getByLabelText('6-digit one-time code'));
    await app.user.paste(code);
    await app.user.click(screen.getByRole('button', { name: 'Verify' }));
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    await waitFor(() => {
      expect(app.queryClient.getMutationCache().getAll()).toHaveLength(0);
    });
    expect(mutationCache(app)).not.toContain(code);
  });

  it('keeps the values out of the URL, hash, history, router state, storage and query keys', async () => {
    const app = await runTheFlow();
    await startFlow(app);
    await app.user.click(screen.getByLabelText('6-digit one-time code'));
    await app.user.paste(code);
    await app.user.click(screen.getByRole('button', { name: 'Verify' }));
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });

    const everywhere = JSON.stringify([
      app.router.state.location,
      await everyHistoryEntry(app.router),
      app.router.history.length,
      window.location.href,
      window.location.hash,
      window.history.state,
      app.queryClient
        .getQueryCache()
        .getAll()
        .map((query) => query.queryKey),
    ]);
    for (const value of [idNumber, accountNumber, code]) expect(everywhere).not.toContain(value);
    expect(app.router.state.location.hash).toBe('');
    expect(localStorage).toHaveLength(0);
    expect(sessionStorage).toHaveLength(0);
  });

  it('puts no submitted value on any browser span attribute', async () => {
    const app = await runTheFlow();
    await startFlow(app);
    await app.user.click(screen.getByLabelText('6-digit one-time code'));
    await app.user.paste(code);
    await app.user.click(screen.getByRole('button', { name: 'Verify' }));
    await screen.findByRole('heading', { level: 1, name: 'Transactions' });
    await flushSpans();

    const spans = endedSpans();
    expect(spans.some((span) => JSON.stringify(span.attributes).includes('linking/start'))).toBe(
      true,
    );
    expect(spans.some((span) => JSON.stringify(span.attributes).includes('linking/verify'))).toBe(
      true,
    );
    const attributes = JSON.stringify(spans.map((span) => span.attributes));
    for (const value of [idNumber, accountNumber, code]) expect(attributes).not.toContain(value);
  });
});
