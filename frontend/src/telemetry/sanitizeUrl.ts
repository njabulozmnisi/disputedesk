const guidSegment = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The `url.full` of a fetch span: origin and path only. The query (a transactions `cursor`) and the hash are dropped,
 * and a GUID path segment (a dispute ID) becomes `{id}`.
 */
export function sanitizeUrl(url: string): string {
  const { origin, pathname } = new URL(url);
  const path = pathname
    .split('/')
    .map((segment) => (guidSegment.test(segment) ? '{id}' : segment))
    .join('/');
  return `${origin}${path}`;
}
