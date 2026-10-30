import type { InfiniteData, QueryClient } from '@tanstack/react-query';
import { useQueryClient } from '@tanstack/react-query';
import { Navigate, Outlet, useParams } from '@tanstack/react-router';
import { createContext, useContext, useState } from 'react';

import { getGetApiV1TransactionsInfiniteQueryKey } from '@/api/generated/disputedesk';
import type { TransactionPageResponse, TransactionResponse } from '@/api/generated/model';
import { SessionGate } from '@/app/SessionGate';

import { useRaiseDraft } from './RaiseDraft';

const RowContext = createContext<TransactionResponse | undefined>(undefined);

/** The transaction being disputed, for 4a and 4b. */
export function useRaiseRow(): TransactionResponse {
  const row = useContext(RowContext);
  if (!row) throw new Error('useRaiseRow needs the raise layout');
  return row;
}

// There is no GET for one transaction, so the row comes from the pages the list query has cached.
function cachedRow(queryClient: QueryClient, transactionId: string) {
  const pages = queryClient.getQueryData<InfiniteData<TransactionPageResponse>>(
    getGetApiV1TransactionsInfiniteQueryKey(),
  )?.pages;
  return pages?.flatMap((page) => page.items).find((row) => row.transactionId === transactionId);
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
  return (
    <RowContext value={row}>
      <Outlet />
    </RowContext>
  );
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
