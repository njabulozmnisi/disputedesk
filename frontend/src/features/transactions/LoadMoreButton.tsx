import { Button } from '@/components/ui/button';

/** "Loading…" with a spinner while the next page is fetched. It stays focusable but cannot be pressed twice. */
export function LoadMoreButton({ loading, onClick }: { loading: boolean; onClick: () => void }) {
  return (
    <Button
      type="button"
      variant="secondary"
      aria-disabled={loading}
      onClick={() => {
        if (!loading) onClick();
      }}
    >
      {loading ? (
        <span
          aria-hidden="true"
          className="size-4 animate-spin rounded-full border-2 border-current border-t-transparent motion-reduce:animate-none"
        />
      ) : null}
      {loading ? 'Loading…' : 'Load more'}
    </Button>
  );
}
