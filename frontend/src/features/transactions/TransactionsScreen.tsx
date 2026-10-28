import { useEffect, useRef } from 'react';

import { useGetApiV1TransactionsInfinite } from '@/api/generated/disputedesk';
import type { TransactionResponse } from '@/api/generated/model';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { Skeleton } from '@/components/ui/skeleton';

import { formatDay } from './format';
import { LoadMoreButton } from './LoadMoreButton';
import { TransactionRow } from './TransactionRow';
import { TransactionsError } from './TransactionsError';

const panelClass = 'rounded-md border border-border bg-card';

function LoadingList() {
  return (
    <ul aria-busy="true" aria-label="Loading transactions" className={panelClass}>
      {[1, 2, 3, 4, 5].map((placeholder) => (
        <li
          key={placeholder}
          className="flex min-h-(--row-min) items-center justify-between gap-4 border-t border-border p-4 first:border-t-0"
        >
          <div className="grid flex-1 gap-2">
            <Skeleton className="w-3/5" />
            <Skeleton className="w-2/5" />
          </div>
          <Skeleton className="w-18" />
        </li>
      ))}
    </ul>
  );
}

/** Rows that were made on the same day (South African time) sit under one heading. Input is newest first. */
function groupByDay(rows: TransactionResponse[]) {
  const days: { day: string; rows: { row: TransactionResponse; index: number }[] }[] = [];
  rows.forEach((row, index) => {
    const day = formatDay(row.transactionDate);
    const last = days.at(-1);
    if (last?.day === day) {
      last.rows.push({ row, index });
    } else {
      days.push({ day, rows: [{ row, index }] });
    }
  });
  return days;
}

export function TransactionsScreen() {
  const headingRef = usePageHeading('Transactions');
  const announce = useAnnounce();
  const query = useGetApiV1TransactionsInfinite(undefined, {
    query: {
      initialPageParam: undefined,
      getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    },
  });

  const rows = query.data?.pages.flatMap((page) => page.items) ?? [];
  const rowElements = useRef(new Map<number, HTMLLIElement>());
  const rowCountBeforeLoadMore = useRef<number | undefined>(undefined);

  // After Load more, focus moves to the first new row, so focus is not lost when the button goes (WCAG 2.2 SC 2.4.3),
  // and the new rows are announced (SC 4.1.3).
  useEffect(() => {
    const before = rowCountBeforeLoadMore.current;
    if (before === undefined || rows.length <= before) return;
    rowCountBeforeLoadMore.current = undefined;
    rowElements.current.get(before)?.focus();
    const added = rows.length - before;
    announce(`${String(added)} more ${added === 1 ? 'transaction' : 'transactions'} loaded`);
  }, [rows.length, announce]);

  function loadMore() {
    rowCountBeforeLoadMore.current = rows.length;
    void query.fetchNextPage();
  }

  return (
    <Page>
      <div className="grid gap-4">
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-title font-title tracking-title wrap-anywhere text-heading"
        >
          Transactions
        </h1>
        <p className="max-w-[68ch] text-muted-foreground">
          Your card and account transactions, newest first.
        </p>
      </div>

      {query.isPending ? <LoadingList /> : null}

      {query.isError && !query.data ? (
        <TransactionsError error={query.error} onRetry={() => void query.refetch()} />
      ) : null}

      {query.data && rows.length === 0 ? (
        <div className="grid gap-3 rounded-lg border border-border bg-card px-6 py-8">
          <h2 className="text-subheading font-strong">No transactions in the last 90 days.</h2>
          <p className="max-w-[68ch] text-muted-foreground">
            Card and account transactions appear here once they reach your account. Disputes you
            raised earlier are still in{' '}
            {/* shortcut: a plain link (a full page load) until the disputes story adds its route */}
            <a href="/disputes">Disputes</a>.
          </p>
        </div>
      ) : null}

      {rows.length > 0 ? (
        <div className={panelClass}>
          {groupByDay(rows).map(({ day, rows: dayRows }) => (
            <section key={day} className="border-t border-border first:border-t-0">
              <h2 className="px-4 pt-4 pb-1 text-label font-strong text-muted-foreground">{day}</h2>
              <ul>
                {dayRows.map(({ row, index }) => (
                  <TransactionRow
                    key={row.transactionId}
                    row={row}
                    rowRef={(element) => {
                      if (element) rowElements.current.set(index, element);
                      return () => {
                        rowElements.current.delete(index);
                      };
                    }}
                  />
                ))}
              </ul>
            </section>
          ))}
        </div>
      ) : null}

      {query.isFetchNextPageError ? (
        <TransactionsError error={query.error} onRetry={loadMore} />
      ) : null}
      {query.hasNextPage ? (
        <div>
          <LoadMoreButton loading={query.isFetchingNextPage} onClick={loadMore} />
        </div>
      ) : null}
    </Page>
  );
}
