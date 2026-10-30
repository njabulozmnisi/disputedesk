import { Navigate } from '@tanstack/react-router';
import type { ReactNode } from 'react';

import { useGetApiV1BffSession } from '@/api/generated/disputedesk';
import type { SessionState } from '@/api/generated/model';
import { traceIdOf } from '@/api/problems';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';

// Where each authorisation state belongs (EXPERIENCE.md, Information Architecture).
const homeOf: Record<SessionState, string> = {
  anonymous: '/',
  unlinked: '/link',
  linked: '/transactions',
};

/**
 * Renders `children` only for the `allow` state. Any other state is sent home. While the session is unknown nothing
 * shows, and if the session request itself failed the person sees an error with "Try again", never the start page.
 */
export function SessionGate({ allow, children }: { allow: SessionState; children: ReactNode }) {
  const session = useGetApiV1BffSession();

  if (!session.data) {
    if (!session.isError) return <div aria-busy="true" />;
    return (
      <Page width="form">
        <ProblemNotice
          message="Something went wrong on our side. Try again."
          traceId={traceIdOf(session.error)}
        />
        <Button onClick={() => void session.refetch()}>Try again</Button>
      </Page>
    );
  }

  if (session.data.state !== allow) return <Navigate to={homeOf[session.data.state]} replace />;
  return children;
}
