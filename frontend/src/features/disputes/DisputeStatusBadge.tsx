import { Badge } from '@/components/ui/badge';

import type { StatusDescription } from './customerStatus';

/**
 * A dispute's customer-facing status. The icon is decoration; the visually hidden prefix gives a link around the badge
 * the name "Dispute status: {label}", so the visible label is part of it (WCAG 2.2 SC 2.5.3, technique C7). The label
 * underlines when the link around the badge, which carries the `group` class, is hovered (mockup app.css, a.badge:hover).
 */
export function DisputeStatusBadge({
  statusDescription,
}: {
  statusDescription: StatusDescription;
}) {
  const Icon = statusDescription.icon;

  return (
    // The label wraps, even inside a word, instead of overflowing a narrow panel at a large text size
    // (WCAG 2.2 SC 1.4.4, 1.4.10).
    <Badge tone={statusDescription.tone} className="whitespace-normal wrap-anywhere">
      <Icon className="size-4 shrink-0" />
      <span className="group-hover:underline">
        <span className="sr-only">Dispute status:</span> {statusDescription.label}
      </span>
    </Badge>
  );
}
