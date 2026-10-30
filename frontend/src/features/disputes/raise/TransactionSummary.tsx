import type { TransactionResponse } from '@/api/generated/model';
import { describeAmount, formatFullDay, showAmount } from '@/features/transactions/format';
import { MaskedNumber } from '@/features/transactions/TransactionRow';

/** The transaction being disputed: description, date, masked number and amount (docs/ux/mockups/dispute-new.html). */
export function TransactionSummary({ row }: { row: TransactionResponse }) {
  return (
    <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-3 rounded-md border border-border bg-card p-4">
      <div className="min-w-0">
        <p className="font-strong wrap-break-word">{row.description}</p>
        <p className="text-caption text-muted-foreground">{formatFullDay(row.transactionDate)}</p>
        <MaskedNumber row={row} />
      </div>
      <span className="text-right text-amount font-strong whitespace-nowrap tabular-nums">
        <span aria-hidden="true">{showAmount(row)}</span>
        <span className="sr-only">{describeAmount(row)}</span>
      </span>
    </div>
  );
}
