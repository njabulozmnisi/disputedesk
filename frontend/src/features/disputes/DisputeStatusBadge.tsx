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
  return (
    <Badge tone={statusDescription.tone}>
      <span aria-hidden="true">{statusDescription.icon}</span>
      <span className="group-hover:underline">
        <span className="sr-only">Dispute status:</span> {statusDescription.label}
      </span>
    </Badge>
  );
}
