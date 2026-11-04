import { useEffect, useRef, type RefObject } from 'react';

export type ErrorSummaryItem = { fieldId: string; message: string };

/**
 * GOV.UK Error summary: a titled group at the top of the form that links to each field with an error. It takes focus
 * each time `requests` changes (one more failed submit or returned 400), and the inner `role="alert"` announces it.
 * `formError` is a line for an error that belongs to no field. A screen that must give the summary focus after its own
 * effects ran passes the `ref` it focuses.
 */
export function ErrorSummary({
  items,
  requests,
  formError,
  ref,
}: {
  items: ErrorSummaryItem[];
  requests: number;
  formError?: string;
  ref?: RefObject<HTMLDivElement | null>;
}) {
  const ownRef = useRef<HTMLDivElement>(null);
  const summaryRef = ref ?? ownRef;
  useEffect(() => {
    summaryRef.current?.focus();
  }, [requests, summaryRef]);

  return (
    <div
      ref={summaryRef}
      role="group"
      aria-labelledby="error-summary-title"
      tabIndex={-1}
      className="rounded-md border-[1.5px] border-destructive bg-status-danger-bg p-4 text-status-danger-fg"
    >
      <div role="alert" className="grid gap-2">
        <h2 id="error-summary-title" className="text-body font-strong">
          Check the details below
        </h2>
        {formError ? <p>{formError}</p> : null}
        <ul className="grid gap-1">
          {items.map(({ fieldId, message }) => (
            <li key={fieldId}>
              <a
                href={`#${fieldId}`}
                onClick={(event) => {
                  event.preventDefault();
                  document.getElementById(fieldId)?.focus();
                }}
              >
                {message}
              </a>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
