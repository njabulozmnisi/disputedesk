import { Link } from '@tanstack/react-router';
import type { Ref } from 'react';

import type { DisputeSummaryResponse } from '@/api/generated/model';
import { formatDayWithYear, formatMoney } from '@/features/transactions/format';

import type { StatusDescription } from '../customerStatus';
import { DisputeStatusBadge } from '../DisputeStatusBadge';
import { reasonCopy } from '../raise/reasons';

/**
 * One dispute. The whole row is the link, so its accessible name carries the description and the status label
 * (WCAG 2.2 SC 2.4.4, 2.5.3). The `li` takes focus after Load more.
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
    <li ref={rowRef} tabIndex={-1} className="border-t border-border first:border-t-0">
      <Link
        to="/disputes/$disputeId"
        params={{ disputeId: dispute.disputeId }}
        className="group grid min-h-(--row-min) grid-cols-[minmax(0,1fr)_auto] items-start gap-x-3 gap-y-2 px-4 py-3 font-normal text-foreground no-underline hover:bg-muted hover:text-foreground md:gap-x-6"
      >
        <div className="min-w-0">
          <p className="font-strong wrap-break-word">{dispute.transaction.description}</p>
          <p className="text-caption text-muted-foreground">
            {reasonLabel ? `${reasonLabel}, raised ` : 'Raised '}
            <time dateTime={dispute.createdAt}>{formatDayWithYear(dispute.createdAt)}</time>
          </p>
        </div>
        <div className="grid justify-items-end gap-2">
          <span className="text-amount font-strong whitespace-nowrap tabular-nums">
            {formatMoney(dispute.disputedAmount)}
          </span>
          <DisputeStatusBadge statusDescription={statusDescription} />
        </div>
      </Link>
    </li>
  );
}
