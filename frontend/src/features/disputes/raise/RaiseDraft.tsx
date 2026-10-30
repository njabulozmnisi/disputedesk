import { createContext, useContext, useState, type ReactNode } from 'react';

import type { RaiseDisputeRequest } from '@/api/generated/model';

import type { RaiseFormValues } from './raiseSchema';

export type FieldName = keyof RaiseFormValues;

/** What a 400 told 4a: a message for each named field, and whether anything else went wrong. */
export type ServerErrors = { fields: Partial<Record<FieldName, string>>; formError: boolean };

/** One draft: the form values, the one frozen request built from them, and its `Idempotency-Key`. */
export type Draft = {
  transactionId: string;
  values: RaiseFormValues;
  request: RaiseDisputeRequest;
  key: string;
};

type RaiseDraftState = {
  draftFor: (transactionId: string) => Draft | undefined;
  isSubmitted: (transactionId: string) => boolean;
  /** Continue: a request equal to the draft's keeps its key; a different one gets a new key. */
  continueWith: (values: RaiseFormValues, request: RaiseDisputeRequest) => void;
  markSubmitted: (transactionId: string) => void;
  serverErrors: ServerErrors | undefined;
  setServerErrors: (errors: ServerErrors | undefined) => void;
};

const RaiseDraftContext = createContext<RaiseDraftState | undefined>(undefined);

// RFC 9651 String: the key is sent in double quotes (AD-16).
const newKey = () => `"${crypto.randomUUID()}"`;

/**
 * The draft lives only in this component's state (EXPERIENCE.md, Back and forward): not in the URL, router state,
 * storage or a query key. It sits above both raise routes so Change keeps the values, and above `/disputes/...` so a
 * submitted draft is remembered when the person presses Back.
 */
export function RaiseDraftProvider({ children }: { children: ReactNode }) {
  const [draft, setDraft] = useState<Draft>();
  const [submittedIds, setSubmittedIds] = useState<ReadonlySet<string>>(new Set());
  const [serverErrors, setServerErrors] = useState<ServerErrors>();

  const state: RaiseDraftState = {
    draftFor: (transactionId) => (draft?.transactionId === transactionId ? draft : undefined),
    isSubmitted: (transactionId) => submittedIds.has(transactionId),
    continueWith: (values, request) => {
      const sameRequest = draft && JSON.stringify(draft.request) === JSON.stringify(request);
      setDraft({
        transactionId: request.transactionId,
        values,
        request,
        key: sameRequest ? draft.key : newKey(),
      });
    },
    markSubmitted: (transactionId) => {
      setSubmittedIds((ids) => new Set(ids).add(transactionId));
    },
    serverErrors,
    setServerErrors,
  };

  return <RaiseDraftContext value={state}>{children}</RaiseDraftContext>;
}

export function useRaiseDraft(): RaiseDraftState {
  const state = useContext(RaiseDraftContext);
  if (!state) throw new Error('useRaiseDraft needs a RaiseDraftProvider');
  return state;
}
