import type { TransactionResponse } from '@/api/generated/model';

/** For `notEligible` the row's own fields say which of the four reasons it is. */
function notEligibleHint(row: TransactionResponse): string {
  if (row.direction === 'credit') return 'Money in. Not disputable.';
  if (row.kind === 'ownAccountTransfer') return 'Transfer between your accounts. Not disputable.';
  if (row.state === 'pending') return 'Pending. You can dispute it once it has settled.';
  return 'Not a card payment. Contact the bank about this transaction.';
}

/**
 * The reason text for a row that is not disputable (EXPERIENCE.md, Copy deck: Not disputable), or undefined when the
 * row is disputable or has no text. The switch is exhaustive: a value added to `NotDisputableReason` fails `tsc`
 * at the `never` line until it is handled here.
 */
export function notDisputableHint(row: TransactionResponse): string | undefined {
  const reason = row.notDisputableReason;
  switch (reason) {
    case null:
      return undefined;
    case 'notEligible':
      return notEligibleHint(row);
    case 'outsideWindow':
      return 'Older than 90 days. Contact the bank about this transaction.';
    case 'alreadyDisputed':
      // No hint: the row shows the dispute's status badge instead.
      return undefined;
    default: {
      // A value this SPA build does not know yet (a newer API) shows no hint instead of breaking the row.
      reason satisfies never;
      return undefined;
    }
  }
}
