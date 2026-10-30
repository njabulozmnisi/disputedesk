import { Link } from '@tanstack/react-router';
import { useEffect, useMemo, useRef } from 'react';

import { useGetApiV1DisputesInfinite } from '@/api/generated/disputedesk';
import type { DisputeSummaryResponse } from '@/api/generated/model';
import { traceIdOf } from '@/api/problems';
import { useAnnounce } from '@/app/LiveRegion';
import { usePageHeading } from '@/app/usePageHeading';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LoadMoreButton } from '@/features/transactions/LoadMoreButton';

import { describeCustomerStatus, type StatusDescription } from '../customerStatus';
import { DisputeRow } from './DisputeRow';

const panelClass = 'rounded-md border border-border bg-card';
const errorMessage = 'Something went wrong on our side. Try again.';

type ListedDispute = { dispute: DisputeSummaryResponse; statusDescription: StatusDescription };

function LoadingList() {
  return (
    <ul aria-busy="true" aria-label="Loading disputes" className={panelClass}>
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

export function DisputesScreen() {
  const headingRef = usePageHeading('Disputes');
  const announce = useAnnounce();
  const query = useGetApiV1DisputesInfinite(undefined, {
    query: {
      initialPageParam: undefined,
      getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    },
  });

  // An item whose status this build does not know is left out, so it cannot show a raw or internal name (AD-13).
  const listed = useMemo(
    () =>
      (query.data?.pages ?? [])
        .flatMap((page) => page.items)
        .flatMap((dispute): ListedDispute[] => {
          const statusDescription = describeCustomerStatus(dispute.customerStatus, dispute.outcome);
          return statusDescription ? [{ dispute, statusDescription }] : [];
        }),
    [query.data],
  );
  const open = listed.filter(({ statusDescription }) => statusDescription.group === 'open');
  const closed = listed.filter(({ statusDescription }) => statusDescription.group === 'closed');

  const rowElements = useRef(new Map<string, HTMLLIElement>());
  const idsBeforeLoadMore = useRef<Set<string> | undefined>(undefined);

  // When a page arrives, focus moves to the first new dispute in API order, found by its ID because a new page can add
  // rows to both sections (WCAG 2.2 SC 2.4.3), and the new rows are announced (SC 4.1.3). The IDs are forgotten once
  // the page is in, shown or not, and when the request fails, so a later refetch is not taken for a page of Load more.
  const isFetchingNextPage = query.isFetchingNextPage;
  useEffect(() => {
    const before = idsBeforeLoadMore.current;
    if (!before || isFetchingNextPage) return;
    idsBeforeLoadMore.current = undefined;
    const added = listed.filter(({ dispute }) => !before.has(dispute.disputeId));
    const [first] = added;
    if (!first) return;
    rowElements.current.get(first.dispute.disputeId)?.focus();
    announce(`${String(added.length)} more ${added.length === 1 ? 'dispute' : 'disputes'} loaded`);
  }, [listed, announce, isFetchingNextPage]);

  async function loadMore() {
    idsBeforeLoadMore.current = new Set(listed.map(({ dispute }) => dispute.disputeId));
    const result = await query.fetchNextPage();
    if (result.isError) idsBeforeLoadMore.current = undefined;
  }

  // The Try again button unmounts on success, so focus goes to the heading (WCAG 2.2 SC 2.4.3).
  function retry() {
    void query.refetch().then((result) => {
      if (result.isSuccess) headingRef.current?.focus();
    });
  }

  function renderSection(heading: string, rows: ListedDispute[]) {
    if (rows.length === 0) return null;
    return (
      <section className="grid gap-3">
        <h2 className="text-subheading font-strong">{heading}</h2>
        <ul className={panelClass}>
          {rows.map(({ dispute, statusDescription }) => (
            <DisputeRow
              key={dispute.disputeId}
              dispute={dispute}
              statusDescription={statusDescription}
              rowRef={(element) => {
                if (element) rowElements.current.set(dispute.disputeId, element);
                return () => {
                  rowElements.current.delete(dispute.disputeId);
                };
              }}
            />
          ))}
        </ul>
      </section>
    );
  }

  return (
    <Page>
      <div className="grid gap-4">
        <h1
          ref={headingRef}
          tabIndex={-1}
          className="text-title font-title tracking-title wrap-anywhere text-heading"
        >
          Disputes
        </h1>
        <p className="max-w-[68ch] text-muted-foreground">
          Every dispute you&apos;ve raised, newest first.
        </p>
      </div>

      {query.isPending ? <LoadingList /> : null}

      {query.isError && !query.data ? (
        <div className="grid gap-6">
          <ProblemNotice message={errorMessage} traceId={traceIdOf(query.error)} />
          <div>
            <Button type="button" onClick={retry}>
              Try again
            </Button>
          </div>
        </div>
      ) : null}

      {query.data && listed.length === 0 && !query.hasNextPage ? (
        <div className="grid gap-3 rounded-lg border border-border bg-card px-6 py-8">
          <h2 className="text-subheading font-strong">
            You haven&apos;t disputed any transactions.
          </h2>
          <p className="max-w-[68ch] text-muted-foreground">
            If something on your account looks wrong, find it in your transactions and choose
            Dispute. It will show here with its status.
          </p>
          <div>
            <Button asChild variant="secondary">
              <Link to="/transactions">Go to transactions</Link>
            </Button>
          </div>
        </div>
      ) : null}

      {renderSection('Open', open)}
      {renderSection('Closed', closed)}

      {query.isFetchNextPageError ? (
        <ProblemNotice message={errorMessage} traceId={traceIdOf(query.error)} />
      ) : null}
      {query.hasNextPage ? (
        <div>
          <LoadMoreButton loading={query.isFetchingNextPage} onClick={() => void loadMore()} />
        </div>
      ) : null}
    </Page>
  );
}
