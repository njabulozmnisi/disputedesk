import { screen } from '@testing-library/react';
import type { UserEvent } from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';

import type { TransactionResponse } from '@/api/generated/model';

import { server } from './server';
import {
  disputeGuid,
  makeDispute,
  makeRow,
  renderApp,
  serveAntiforgeryToken,
  serveDispute,
  serveTransactions,
  signInAs,
} from './support';

export type RaiseRequest = { headers: Headers; body: Record<string, unknown> };

export const created = () =>
  HttpResponse.json(makeDispute(), {
    status: 201,
    headers: { Location: `/api/v1/disputes/${disputeGuid}` },
  });

/** Answers `POST /api/v1/disputes` with `respond(attempt)` (1 for the first request) and records every request. */
export function recordRaiseRequests(respond: (attempt: number) => Response = created) {
  const requests: RaiseRequest[] = [];
  server.use(
    http.post('*/api/v1/disputes', async ({ request }) => {
      requests.push({
        headers: request.headers,
        body: (await request.json()) as Record<string, unknown>,
      });
      return respond(requests.length);
    }),
  );
  return requests;
}

/** Signs in a linked customer, lists `rows` and opens screen 4a from the first row's Dispute control. */
export async function openRaiseScreen(
  rows: TransactionResponse[] = [makeRow()],
  { fakeTimers = false, strictMode = false } = {},
) {
  signInAs('linked');
  serveAntiforgeryToken();
  serveTransactions(rows);
  serveDispute();
  const app = renderApp({ path: '/transactions', fakeTimers, strictMode });
  await app.user.click(await screen.findByRole('link', { name: 'Dispute this transaction' }));
  await screen.findByRole('heading', { level: 1, name: 'Dispute this transaction' });
  return app;
}

/** Picks a reason on 4a by its label. The caller fills the expected amount for Wrong amount. */
export async function chooseReason(user: UserEvent, label: string) {
  await user.click(screen.getByRole('radio', { name: label }));
}

export async function continueToReview(user: UserEvent) {
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  await screen.findByRole('heading', { level: 1, name: 'Review your dispute' });
}

/** Opens 4a, picks Unrecognised, continues, and returns the app on screen 4b. */
export async function openReviewScreen(options?: { fakeTimers?: boolean; strictMode?: boolean }) {
  const app = await openRaiseScreen([makeRow()], options);
  await chooseReason(app.user, 'Unrecognised');
  await continueToReview(app.user);
  return app;
}
