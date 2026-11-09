import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import type { Ref } from 'react';

import type { DisputeSummaryResponse } from '@/api/generated/model';
import { formatDayWithYear, formatMoney } from '@/features/transactions/format';

import type { StatusDescription } from '../customerStatus';
import { DisputeStatusBadge } from '../DisputeStatusBadge';
import { reasonCopy } from '../raise/reasons';

/**
 * One dispute. The whole row is the link, so its accessible name carries the description and the status label
 * (WCAG 2.2 SC 2.4.4, 2.5.3). The `li` takes focus after Load more. From 768px the row uses the transactions' columns
 * and a chevron column of 1.25rem (--spacing(5)); the panel clips its content, so both focus rings are drawn inside.
 * The row's own group name, group/row, keeps the badge's plain group-hover:underline from firing: a hovered row
 * underlines the merchant only (Tailwind, Differentiating nested groups).
 */
export function DisputeRow({
  dispute,
  statusDescription,
  rowRef,
}: {
  dispute: DisputeSummaryResponse;
  statusDescription: StatusDescription;
  rowRef: Ref<HTMLLIElement>;
}) {
  // A reason this build does not know yet has no label; the raw value is never shown.
  const reasonLabel = Object.hasOwn(reasonCopy, dispute.reason)
    ? reasonCopy[dispute.reason].label
    : undefined;

  return (
    <li
      ref={rowRef}
      tabIndex={-1}
      className="border-t border-border first:border-t-0 focus-visible:-outline-offset-3"
    >
      <Link
        to="/disputes/$disputeId"
        params={{ disputeId: dispute.disputeId }}
        className="group/row grid min-h-(--row-min) grid-cols-[minmax(0,1fr)_auto] items-start gap-x-3 gap-y-1 px-4 py-3 font-normal text-foreground no-underline hover:bg-background hover:text-foreground focus-visible:-outline-offset-3 md:grid-cols-[minmax(0,1fr)_var(--amount-col)_var(--action-col)_--spacing(5)] md:items-center md:gap-x-6 md:px-6"
      >
        <div className="col-start-1 row-span-2 row-start-1 min-w-0 md:row-span-1">
          <p className="font-strong wrap-break-word text-primary group-hover/row:underline">
            {dispute.transaction.description}
          </p>
          <p className="text-caption text-muted-foreground">
            {reasonLabel ? `${reasonLabel}, raised ` : 'Raised '}
            <time dateTime={dispute.createdAt}>{formatDayWithYear(dispute.createdAt)}</time>
          </p>
        </div>
        <p className="col-start-2 row-start-1 justify-self-end text-right text-amount font-strong whitespace-nowrap tabular-nums">
          {formatMoney(dispute.disputedAmount)}
        </p>
        <div className="col-span-2 row-start-3 justify-self-end md:col-span-1 md:col-start-3 md:row-start-1 md:justify-self-start">
          <DisputeStatusBadge statusDescription={statusDescription} />
        </div>
        <ChevronRight className="hidden size-4 text-muted-foreground md:col-start-4 md:row-start-1 md:block" />
      </Link>
    </li>
  );
}
