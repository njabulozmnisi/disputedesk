import type { SpanContext } from '@opentelemetry/api';
import type { InfiniteData, QueryClient } from '@tanstack/react-query';
import { useQueryClient } from '@tanstack/react-query';
import { Navigate, Outlet, useParams } from '@tanstack/react-router';
import { createContext, useContext, useEffect, useRef, useState, type RefObject } from 'react';

import { getGetApiV1TransactionsInfiniteQueryKey } from '@/api/generated/disputedesk';
import type { TransactionPageResponse, TransactionResponse } from '@/api/generated/model';
import { SessionGate } from '@/app/SessionGate';
import { recordRaiseOpen } from '@/telemetry/raiseSpans';

import { useRaiseDraft } from './RaiseDraft';

const RowContext = createContext<TransactionResponse | undefined>(undefined);

/** The transaction being disputed, for 4a and 4b. */
export function useRaiseRow(): TransactionResponse {
  const row = useContext(RowContext);
  if (!row) throw new Error('useRaiseRow needs the raise layout');
  return row;
}

const OpenSpanContext = createContext<RefObject<SpanContext | undefined> | undefined>(undefined);

/** The context of this flow's `dispute.raise.open` span, which each submit span links to. Read it when submitting. */
export function useRaiseOpenSpan(): RefObject<SpanContext | undefined> {
  const openSpan = useContext(OpenSpanContext);
  if (!openSpan) throw new Error('useRaiseOpenSpan needs the raise layout');
  return openSpan;
}

// There is no GET for one transaction, so the row comes from the pages the list query has cached.
function cachedRow(queryClient: QueryClient, transactionId: string) {
  const pages = queryClient.getQueryData<InfiniteData<TransactionPageResponse>>(
    getGetApiV1TransactionsInfiniteQueryKey(),
  )?.pages;
  return pages?.flatMap((page) => page.items).find((row) => row.transactionId === transactionId);
}

/** Mounts when the guard admits a row: records the open span once, and keeps its context while 4a and 4b are shown. */
function AdmittedRaise({ row }: { row: TransactionResponse }) {
  const openSpan = useRef<SpanContext | undefined>(undefined);
  // The ref is kept across StrictMode's development re-run of this effect, so the span is recorded once.
  useEffect(() => {
    openSpan.current ??= recordRaiseOpen();
  }, []);
  return (
    <RowContext value={row}>
      <OpenSpanContext value={openSpan}>
        <Outlet />
      </OpenSpanContext>
    </RowContext>
  );
}

function RaiseGuard({ transactionId }: { transactionId: string }) {
  const queryClient = useQueryClient();
  const { isSubmitted } = useRaiseDraft();
  // Read once, when the route opens: the row stays on screen even if the list's cache entry is collected.
  const [row] = useState(() => cachedRow(queryClient, transactionId));

  // A deep link, a reload, a row that is not disputable, or a draft that was already submitted (Back after submit).
  if (!row?.disputable || isSubmitted(transactionId)) {
    return <Navigate to="/transactions" replace />;
  }
  return <AdmittedRaise row={row} />;
}

/** The parent of 4a and 4b: for a linked customer, the cached disputable row, or a redirect to the list. */
export function RaiseLayout() {
  const { transactionId = '' } = useParams({ strict: false });
  return (
    <SessionGate allow="linked">
      <RaiseGuard key={transactionId} transactionId={transactionId} />
    </SessionGate>
  );
}
