import { createContext, useContext, useRef, useState, type ReactNode } from 'react';

type Announce = (message: string) => void;

const AnnounceContext = createContext<Announce | undefined>(undefined);

/** The one polite live region (EXPERIENCE.md, Accessibility Floor). Screens announce through `useAnnounce`. */
export function LiveRegionProvider({ children }: { children: ReactNode }) {
  const [announcements, setAnnouncements] = useState<{ id: number; message: string }[]>([]);
  const nextId = useRef(0);

  // Each message is a new element, so a screen reader announces it even when it repeats the last one.
  const announce: Announce = (message) => {
    nextId.current += 1;
    setAnnouncements([{ id: nextId.current, message }]);
  };

  return (
    <AnnounceContext value={announce}>
      {children}
      <div role="status" aria-live="polite" className="sr-only">
        {announcements.map(({ id, message }) => (
          <p key={id}>{message}</p>
        ))}
      </div>
    </AnnounceContext>
  );
}

export function useAnnounce(): Announce {
  const announce = useContext(AnnounceContext);
  if (!announce) throw new Error('useAnnounce needs a LiveRegionProvider');
  return announce;
}
