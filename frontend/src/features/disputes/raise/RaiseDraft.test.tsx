import { act, renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';

import { Reason } from '@/api/generated/model';

import { RaiseDraftProvider, useRaiseDraft } from './RaiseDraft';

const values = { reason: Reason.unrecognised, note: '', expectedAmount: '' };
const request = { accountRef: 'acc-1', transactionId: 'tx-1', reason: Reason.unrecognised };

function renderDraft() {
  return renderHook(useRaiseDraft, {
    wrapper: ({ children }: { children: ReactNode }) => (
      <RaiseDraftProvider>{children}</RaiseDraftProvider>
    ),
  });
}

describe('RaiseDraftProvider', () => {
  it('holds the draft for its transaction only', () => {
    const view = renderDraft();

    act(() => {
      view.result.current.continueWith(values, request);
    });

    expect(view.result.current.draftFor('tx-1')?.request).toEqual(request);
    expect(view.result.current.draftFor('tx-2')).toBeUndefined();
  });

  it('keeps the Idempotency-Key for an equal request and issues a new one for a different request', () => {
    const view = renderDraft();
    act(() => {
      view.result.current.continueWith(values, request);
    });
    const firstKey = view.result.current.draftFor('tx-1')?.key;

    act(() => {
      view.result.current.continueWith(values, { ...request });
    });
    expect(view.result.current.draftFor('tx-1')?.key).toBe(firstKey);

    act(() => {
      view.result.current.continueWith(values, { ...request, reason: Reason.wrongAmount });
    });
    expect(view.result.current.draftFor('tx-1')?.key).not.toBe(firstKey);
  });

  it('remembers which transactions were submitted', () => {
    const view = renderDraft();

    act(() => {
      view.result.current.markSubmitted('tx-1');
    });

    expect(view.result.current.isSubmitted('tx-1')).toBe(true);
    expect(view.result.current.isSubmitted('tx-2')).toBe(false);
  });
});
