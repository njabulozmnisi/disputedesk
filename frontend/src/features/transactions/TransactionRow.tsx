import { Link } from '@tanstack/react-router';
import type { Ref } from 'react';

import type { TransactionResponse } from '@/api/generated/model';

import { describeCustomerStatus } from '@/features/disputes/customerStatus';
import { DisputeStatusBadge } from '@/features/disputes/DisputeStatusBadge';

import { describeAmount, showAmount } from './format';
import { notDisputableHint } from './notDisputableHint';

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
      className="grid min-h-(--row-min) grid-cols-[minmax(0,1fr)_auto] items-start gap-x-3 gap-y-1 border-t border-border px-4 py-3 first:border-t-0 md:gap-x-6"
    >
      <div className="row-span-2 min-w-0">
        <p className="font-strong wrap-break-word">{row.description}</p>
        <MaskedNumber row={row} />
        {hint ? (
          <p className="text-caption text-status-neutral-fg">
            <span aria-hidden="true">ⓘ </span>
            {hint}
          </p>
        ) : null}
      </div>
      <span className="col-start-2 row-start-1 justify-self-end text-right text-amount font-strong whitespace-nowrap tabular-nums">
        <span aria-hidden="true">{showAmount(row)}</span>
        <span className="sr-only">{describeAmount(row)}</span>
      </span>
      {row.disputable ? (
        <Link
          to="/transactions/$transactionId/dispute"
          params={{ transactionId: row.transactionId }}
          className="col-start-2 row-start-2 justify-self-end py-2 text-label font-strong"
        >
          Dispute <span className="sr-only">this transaction</span>
        </Link>
      ) : null}
      {row.disputeId && statusDescription ? (
        // On a phone the badge has its own right-aligned line under the row text; from 768px it is the action column.
        <Link
          to="/disputes/$disputeId"
          params={{ disputeId: row.disputeId }}
          className="col-span-2 row-start-3 group justify-self-end rounded-full py-2 md:col-span-1 md:col-start-2 md:row-start-2"
        >
          <DisputeStatusBadge statusDescription={statusDescription} />
        </Link>
      ) : null}
    </li>
  );
}
