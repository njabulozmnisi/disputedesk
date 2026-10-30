import { useEffect, useRef } from 'react';

export type ErrorSummaryItem = { fieldId: string; message: string };

/**
 * GOV.UK Error summary: a titled group at the top of the form that links to each field with an error. It takes focus
 * each time `requests` changes (one more failed submit or returned 400), and the inner `role="alert"` announces it.
 */
export function ErrorSummary({ items, requests }: { items: ErrorSummaryItem[]; requests: number }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.focus();
  }, [requests]);

  return (
    <div
      ref={ref}
      role="group"
      aria-labelledby="error-summary-title"
      tabIndex={-1}
      className="rounded-md border-[1.5px] border-destructive bg-status-danger-bg p-4 text-status-danger-fg"
    >
      <div role="alert" className="grid gap-2">
        <h2 id="error-summary-title" className="text-body font-strong">
          Check the details below
        </h2>
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
