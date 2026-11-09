import { useEffect, useRef, type RefObject } from 'react';

import { Alert } from '@/components/ui/alert';

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
    // The danger `Alert`'s look and icon, with the group role in place of its alert role (the registry's `Alert` lets a
    // caller's `role` win): the summary takes focus, and the inner element is the one announcement.
    <Alert
      ref={summaryRef}
      tone="danger"
      role="group"
      aria-labelledby="error-summary-title"
      tabIndex={-1}
    >
      <div role="alert" className="col-start-2 grid gap-2">
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
    </Alert>
  );
}
