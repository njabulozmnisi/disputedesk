import { Link } from '@tanstack/react-router';
import { List } from 'lucide-react';
import { Fragment, useEffect, useRef } from 'react';

import { useListTransactionsInfinite } from '@/api/generated/disputedesk';
import type { TransactionResponse } from '@/api/generated/model';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { StateSurface } from '@/components/StateSurface';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';

import { formatDay } from './format';
import { LoadMoreButton } from './LoadMoreButton';
import { ledgerColumns, ledgerRow, TransactionRow } from './TransactionRow';
import { TransactionsError } from './TransactionsError';

// The panel clips its content, so a band's fill and a row's hover fill follow its rounded corners.
const panelClass = 'overflow-hidden rounded-md border border-border bg-card';
// A day band: the page fill, with a hairline above. On a phone the first band has no header above it, so no hairline.
const bandClass =
  'border-t border-border bg-background px-4 py-2 text-label font-strong text-muted-foreground first-of-type:max-md:border-t-0 md:px-6';

/** From 768px the panel opens with the column names. Each row already reads in full, so the header is hidden from assistive technology. */
function ColumnHeader() {
  return (
    <div
      aria-hidden="true"
      className={cn(
        'hidden px-6 py-2.5 text-caption font-strong text-muted-foreground md:grid',
        ledgerColumns,
      )}
    >
      <span>Transaction</span>
      <span className="text-right">Amount</span>
      <span>Dispute</span>
    </div>
  );
}

// Two days, five rows. The bands are decoration and the list exposes only its rows. The name is on the list: a div
// has the generic role, which cannot be named (WAI-ARIA 1.2).
const skeletonDays = [
  [1, 2],
  [3, 4, 5],
];

function LoadingList() {
  return (
    <div className={panelClass}>
      <ColumnHeader />
      <ul aria-busy="true" aria-label="Loading transactions">
        {skeletonDays.map((placeholders) => (
          <Fragment key={placeholders.join()}>
            <li aria-hidden="true" className={bandClass}>
              <Skeleton className="h-3 w-22" />
            </li>
            {placeholders.map((placeholder) => (
              <li key={placeholder} className={ledgerRow}>
                <div className="col-start-1 row-span-2 row-start-1 grid gap-2 md:row-span-1">
                  <Skeleton className="w-3/5" />
                  <Skeleton className="h-2.5 w-2/5" />
                </div>
                <Skeleton className="col-start-2 row-start-1 w-18 justify-self-end" />
                <Skeleton className="col-start-2 row-start-2 w-16 justify-self-end md:col-start-3 md:row-start-1 md:justify-self-start" />
              </li>
            ))}
          </Fragment>
        ))}
      </ul>
    </div>
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
  const query = useListTransactionsInfinite(undefined, {
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

  // The Try again button unmounts on success, so focus goes to the heading (WCAG 2.2 SC 2.4.3).
  function retry() {
    void query.refetch().then((result) => {
      if (result.isSuccess) headingRef.current?.focus();
    });
  }

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
          Your card and account transactions, newest first. You can dispute those from the last 90
          days.
        </p>
      </div>

      {query.isPending ? <LoadingList /> : null}

      {query.isError && !query.data ? (
        <TransactionsError error={query.error} onRetry={retry} />
      ) : null}

      {query.data && rows.length === 0 ? (
        <StateSurface
          icon={List}
          heading={
            <h2 className="text-subheading font-strong">No transactions in the last 90 days.</h2>
          }
        >
          <p className="max-w-[68ch] text-muted-foreground">
            Card and account transactions appear here once they reach your account. Disputes you
            raised earlier are still in <Link to="/disputes">Disputes</Link>.
          </p>
        </StateSurface>
      ) : null}

      {rows.length > 0 ? (
        <div className={panelClass}>
          <ColumnHeader />
          {groupByDay(rows).map(({ day, rows: dayRows }) => (
            <Fragment key={day}>
              <h2 className={bandClass}>{day}</h2>
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
            </Fragment>
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
