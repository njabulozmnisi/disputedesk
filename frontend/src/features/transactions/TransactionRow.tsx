import type { Ref } from 'react';

import type { TransactionResponse } from '@/api/generated/model';

import { describeAmount, showAmount } from './format';
import { notDisputableHint } from './notDisputableHint';

/** "Card 492345•• •••• 3456" for a card, "Account •••• 4821" otherwise. Screen readers get the short form. */
function MaskedNumber({ row }: { row: TransactionResponse }) {
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
 * One transaction. The row is not a link as a whole: its only action is the trailing Dispute control, shown for
 * a disputable row. A row that is not disputable says why in text (EXPERIENCE.md, Component Patterns).
 */
export function TransactionRow({
  row,
  rowRef,
}: {
  row: TransactionResponse;
  rowRef: Ref<HTMLLIElement>;
}) {
  const hint = notDisputableHint(row);

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
        // shortcut: a plain link (a full page load) until the raise story adds its route
        <a
          href={`/transactions/${encodeURIComponent(row.transactionId)}/dispute`}
          className="col-start-2 row-start-2 justify-self-end py-2 text-label font-strong"
        >
          Dispute <span className="sr-only">this transaction</span>
        </a>
      ) : null}
    </li>
  );
}
