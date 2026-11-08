import { useState, type ReactNode } from 'react';

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';

const copiedConfirmationMs = 2000;

type ProblemNoticeProps = {
  /** The plain sentence that says what happened. */
  message: string;
  /** The next step, when the message does not already say it. */
  nextStep?: string;
  /** The reference to quote to the bank: the problem's `traceId`, when the response had one. */
  traceId?: string;
  /** An action the person can take about the problem, such as a retry button. */
  action?: ReactNode;
};

/**
 * EXPERIENCE.md, Component Patterns: a danger `Alert` with the reference on its own row, after a faint hairline,
 * with a Copy button.
 */
export function ProblemNotice({ message, nextStep, traceId, action }: ProblemNoticeProps) {
  const [copied, setCopied] = useState(false);

  async function copyReference() {
    if (!traceId) return;
    try {
      await navigator.clipboard.writeText(traceId);
    } catch {
      return; // The button keeps saying "Copy", so nothing claims the reference was copied.
    }
    setCopied(true);
    setTimeout(() => {
      setCopied(false);
    }, copiedConfirmationMs);
  }

  return (
    <Alert tone="danger">
      <AlertTitle>{message}</AlertTitle>
      <AlertDescription>
        {nextStep ? <p>{nextStep}</p> : null}
        {traceId ? (
          <>
            <p className="text-caption">
              If this keeps happening, contact the bank and quote this reference.
            </p>
            <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-2 justify-self-stretch border-t border-current/20 pt-3">
              <span className="text-caption font-strong">Reference</span>
              <span className="font-mono text-label wrap-anywhere">{traceId}</span>
              <Button
                type="button"
                variant="secondary"
                size="compact"
                className="text-label"
                onClick={() => void copyReference()}
              >
                {copied ? 'Copied' : 'Copy'}
              </Button>
            </div>
          </>
        ) : null}
        {action ? <div className="mt-2">{action}</div> : null}
      </AlertDescription>
    </Alert>
  );
}
