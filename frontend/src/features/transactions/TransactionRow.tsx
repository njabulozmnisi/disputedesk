import { Link } from '@tanstack/react-router';
import { ChevronRight, Info } from 'lucide-react';
import type { Ref } from 'react';

import type { TransactionResponse } from '@/api/generated/model';

import { describeCustomerStatus } from '@/features/disputes/customerStatus';
import { DisputeStatusBadge } from '@/features/disputes/DisputeStatusBadge';
import { cn } from '@/lib/utils';

import { describeAmount, showAmount } from './format';
import { notDisputableHint } from './notDisputableHint';

/**
 * The list's columns from 768px (HANDOFF 3.2): text, amount, action. The header, every row and the skeleton rows use
 * them, so every amount and action lines up down the panel. Below 768px a row has two columns and the action sits
 * under the amount.
 */
export const ledgerColumns =
  'md:grid-cols-[minmax(0,1fr)_var(--amount-col)_var(--action-col)] md:gap-x-6';

/** One row of the panel: the grid, the hairline above it and its padding. The first row's hairline closes its day band. */
export const ledgerRow = cn(
  'grid min-h-(--row-min) grid-cols-[minmax(0,1fr)_auto] items-start gap-x-3 gap-y-1 border-t border-border px-4 py-3 md:items-center md:px-6',
  ledgerColumns,
);

/** "Card 492345•• •••• 3456" for a card, "Account •••• 4821" otherwise. Screen readers get the short form. */
export function MaskedNumber({
  row,
}: {
  row: Pick<TransactionResponse, 'maskedCard' | 'accountLast4'>;
}) {
  if (row.maskedCard) {
    const firstSix = row.maskedCard.slice(0, 6);
    const lastFour = row.maskedCard.slice(-4);
    return (
      <p className="text-caption text-muted-foreground">
        <span aria-hidden="true">{`Card ${firstSix}•• •••• ${lastFour}`}</span>
        <span className="sr-only">{`card ending ${lastFour}`}</span>
      </p>
    );
  }
  return (
    <p className="text-caption text-muted-foreground">
      <span aria-hidden="true">{`Account •••• ${row.accountLast4}`}</span>
      <span className="sr-only">{`account ending ${row.accountLast4}`}</span>
    </p>
  );
}

/**
 * One transaction. The row is not a link as a whole: its only trailing control is the Dispute link on a disputable
 * row, or the dispute's status badge on a disputed one. Any other row says why it is not disputable in text
 * (EXPERIENCE.md, Component Patterns).
 */
export function TransactionRow({
  row,
  rowRef,
}: {
  row: TransactionResponse;
  rowRef: Ref<HTMLLIElement>;
}) {
  const hint = notDisputableHint(row);
  // A missing or empty disputeId means no dispute; an unknown status shows no badge (defence in depth).
  const statusDescription = row.customerStatus
    ? describeCustomerStatus(row.customerStatus)
    : undefined;

  return (
    <li
      ref={rowRef}
      tabIndex={-1}
      // The panel clips its content, so the ring of a row that takes focus after Load more is drawn inside it.
      className={cn(ledgerRow, 'focus-visible:-outline-offset-3')}
    >
      <div className="col-start-1 row-span-2 row-start-1 min-w-0 md:row-span-1">
        <p className="font-strong wrap-break-word">{row.description}</p>
        <MaskedNumber row={row} />
        {hint ? (
          <p className="flex items-start gap-1.5 text-caption text-status-neutral-fg">
            <Info className="mt-0.5 size-3.5 shrink-0" />
            {hint}
          </p>
        ) : null}
      </div>
      <span className="col-start-2 row-start-1 justify-self-end text-right text-amount font-strong whitespace-nowrap tabular-nums">
        <span aria-hidden="true">{showAmount(row)}</span>
        <span className="sr-only">{describeAmount(row)}</span>
      </span>
      {row.disputable ? (
        // The link is 44px tall (WCAG 2.2 SC 2.5.5). On a phone its negative margins let it overlap the row's padding,
        // so the row stays compact. The merchant is read after "Dispute" (SC 2.5.3).
        <Link
          to="/transactions/$transactionId/dispute"
          params={{ transactionId: row.transactionId }}
          className="col-start-2 row-start-2 -my-3 -mr-1 inline-flex min-h-(--tap-target) items-center gap-1 justify-self-end px-1 text-label font-strong no-underline hover:underline focus-visible:-outline-offset-3 md:col-start-3 md:row-start-1 md:m-0 md:justify-self-start"
        >
          Dispute <span className="sr-only">{row.description}</span>
          <ChevronRight className="size-4" />
        </Link>
      ) : null}
      {row.disputeId && statusDescription ? (
        // On a phone the badge has its own right-aligned line under the row text; from 768px it is the action column.
        <Link
          to="/disputes/$disputeId"
          params={{ disputeId: row.disputeId }}
          className="group col-span-2 row-start-3 inline-flex min-h-(--tap-target) items-center justify-self-end rounded-full md:col-span-1 md:col-start-3 md:row-start-1 md:justify-self-start"
        >
          <DisputeStatusBadge statusDescription={statusDescription} />
        </Link>
      ) : null}
    </li>
  );
}
