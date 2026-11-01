import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';

import { Reason } from '@/api/generated/model';

import { RaiseDraftProvider, useRaiseDraft } from './RaiseDraft';

const values = { reason: Reason.unrecognised, note: '', expectedAmount: '' };
const request = { accountRef: 'acc-1', transactionId: 'tx-1', reason: Reason.unrecognised };

/** Renders the hook inside the provider; `rerender({ signedIn })` changes the provider's prop. */
function renderDraft() {
  return renderHook(useRaiseDraft, {
    wrapper: ({ children }: { children: ReactNode }) => (
      <RaiseDraftProvider signedIn={signedInNow}>{children}</RaiseDraftProvider>
    ),
  });
}

let signedInNow = true;

function setUp() {
  signedInNow = true;
  const view = renderDraft();
  act(() => {
    view.result.current.continueWith(values, request);
    view.result.current.markSubmitted('tx-1');
    view.result.current.setServerErrors({ fields: { note: 'Too long.' }, formError: true });
  });
  expect(view.result.current.draftFor('tx-1')).toBeDefined();
  expect(view.result.current.isSubmitted('tx-1')).toBe(true);
  expect(view.result.current.serverErrors).toBeDefined();
  return view;
}

function changeSignedIn(view: ReturnType<typeof renderDraft>, signedIn: boolean) {
  signedInNow = signedIn;
  view.rerender();
}

describe('RaiseDraftProvider', () => {
  it('clears the draft when the session stops being signed in', () => {
    const view = setUp();

    changeSignedIn(view, false);

    expect(view.result.current.draftFor('tx-1')).toBeUndefined();
  });

  it('forgets the submitted transactions when the session stops being signed in', () => {
    const view = setUp();

    changeSignedIn(view, false);

    expect(view.result.current.isSubmitted('tx-1')).toBe(false);
  });

  it('clears the server errors when the session stops being signed in', () => {
    const view = setUp();

    changeSignedIn(view, false);

    expect(view.result.current.serverErrors).toBeUndefined();
  });

  it('keeps everything while the session stays signed in', () => {
    const view = setUp();

    changeSignedIn(view, true);

    expect(view.result.current.draftFor('tx-1')).toBeDefined();
    expect(view.result.current.isSubmitted('tx-1')).toBe(true);
    expect(view.result.current.serverErrors).toBeDefined();
  });
});
