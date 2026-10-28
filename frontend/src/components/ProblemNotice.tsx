import { useState } from 'react';

const copiedConfirmationMs = 2000;

type ProblemNoticeProps = {
  /** The plain sentence that says what happened. */
  message: string;
  /** The next step, when the message does not already say it. */
  nextStep?: string;
  /** The reference to quote to the bank: the problem's `traceId`, when the response had one. */
  traceId?: string;
};

/** EXPERIENCE.md, Component Patterns: a danger-tone notice with the reference and a Copy button. */
export function ProblemNotice({ message, nextStep, traceId }: ProblemNoticeProps) {
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
    <div
      role="alert"
      className="grid gap-2 rounded-md bg-status-danger-bg p-4 text-status-danger-fg"
    >
      <strong className="font-strong">{message}</strong>
      {nextStep ? <span>{nextStep}</span> : null}
      {traceId ? (
        <p className="text-caption">
          If this keeps happening, contact the bank and quote this reference:
          <br />
          <span className="font-mono text-label break-all">{traceId}</span>
          <button
            type="button"
            className="ml-2 cursor-pointer rounded-full border-[1.5px] border-current px-3 py-1 text-caption font-strong"
            onClick={() => void copyReference()}
          >
            {copied ? 'Copied' : 'Copy'}
          </button>
        </p>
      ) : null}
    </div>
  );
}
