import type { QueryClient } from '@tanstack/react-query';
import {
  createRootRouteWithContext,
  createRoute,
  createRouter,
  notFound,
  redirect,
  useRouter,
  type ErrorComponentProps,
  type RouterHistory,
} from '@tanstack/react-router';

import { getSession, getGetSessionQueryOptions } from '@/api/generated/disputedesk';
import type { SessionState } from '@/api/generated/model';
import { traceIdOf } from '@/api/problems';
import { Page } from '@/components/Page';
import { ProblemNotice } from '@/components/ProblemNotice';
import { Button } from '@/components/ui/button';
import { NotFoundPage } from '@/pages/NotFoundPage';
import { SignedOutPage } from '@/pages/SignedOutPage';
import { StartPage } from '@/pages/StartPage';
import { DisputeDetailScreen } from '@/features/disputes/detail/DisputeDetailScreen';
import { DisputesScreen } from '@/features/disputes/history/DisputesScreen';
import { DetailsScreen } from '@/features/linking/DetailsScreen';
import { OtpScreen } from '@/features/linking/OtpScreen';
import { RaiseLayout } from '@/features/disputes/raise/RaiseLayout';
import { RaiseScreen } from '@/features/disputes/raise/RaiseScreen';
import { ReviewScreen } from '@/features/disputes/raise/ReviewScreen';
import { TransactionsScreen } from '@/features/transactions/TransactionsScreen';

import { RootLayout } from './RootLayout';

// Routes are defined with TanStack Router (AD-31). `/signed-out` and the not-found route are public. Every other route
// sits under one of three pathless layout routes, one per authorisation state, whose `beforeLoad` sends any other
// state to its home (TanStack Router, Authenticated Routes). The raise draft lives in RootLayout around every route,
// so Change keeps its values and Back after a submit finds it submitted.
const rootRoute = createRootRouteWithContext<{ queryClient: QueryClient }>()({
  component: RootLayout,
  notFoundComponent: NotFoundPage,
});

// Where each authorisation state belongs (EXPERIENCE.md, Information Architecture).
const homeOf = {
  anonymous: '/',
  unlinked: '/link',
  linked: '/transactions',
} as const satisfies Record<SessionState, string>;

// While the session is unknown a layout shows nothing, and if the session request fails it shows an error with "Try
// again", never the start page. Both show at once: `pendingMs: 0` and `pendingMinMs: 0`, because the router's defaults
// would show nothing for a second and then hold the busy state for half a second (TanStack Router, Data Loading).
function SessionPending() {
  return <div aria-busy="true" />;
}

// "Try again" runs the guard again (TanStack Router, Data Loading: `router.invalidate()` reloads the matches).
function SessionError({ error }: ErrorComponentProps) {
  const router = useRouter();
  return (
    <Page width="form">
      <ProblemNotice
        message="Something went wrong on our side. Try again."
        traceId={traceIdOf(error)}
      />
      <Button
        onClick={() => {
          void router.invalidate();
        }}
      >
        Try again
      </Button>
    </Page>
  );
}

function sessionLayout<Id extends string>(id: Id, ownState: SessionState) {
  return createRoute({
    getParentRoute: () => rootRoute,
    id,
    beforeLoad: async ({ context }) => {
      // `query` with `staleTime: 'static'` returns a cached session as it is. Its `queryFn` does not read `signal`, so
      // an observer that leaves mid-read does not cancel it (TanStack Query, Query Cancellation).
      const session = await context.queryClient.query({
        ...getGetSessionQueryOptions({ query: { queryFn: () => getSession() } }),
        staleTime: 'static',
      });
      if (session.state !== ownState) throw redirect({ to: homeOf[session.state], replace: true });
    },
    pendingComponent: SessionPending,
    pendingMs: 0,
    pendingMinMs: 0,
    errorComponent: SessionError,
  });
}
const anonymousRoute = sessionLayout('_anonymous', 'anonymous');
const unlinkedRoute = sessionLayout('_unlinked', 'unlinked');
const linkedRoute = sessionLayout('_linked', 'linked');

// Anonymous people see the start page; anyone signed in is sent to their state's home.
const startRoute = createRoute({
  getParentRoute: () => anonymousRoute,
  path: '/',
  component: StartPage,
});

// 2a and 2b are for an unlinked login only. 2b keeps nothing from 2a: the challenge is held by the server (AD-12).
const linkRoute = createRoute({
  getParentRoute: () => unlinkedRoute,
  path: '/link',
  component: DetailsScreen,
});

const linkOtpRoute = createRoute({
  getParentRoute: () => unlinkedRoute,
  path: '/link/otp',
  component: OtpScreen,
});

const transactionsRoute = createRoute({
  getParentRoute: () => linkedRoute,
  path: '/transactions',
  component: TransactionsScreen,
});

const disputesRoute = createRoute({
  getParentRoute: () => linkedRoute,
  path: '/disputes',
  component: DisputesScreen,
});

// The router decodes path params and the generated client puts `disputeId` into the request path unencoded, so only
// a canonical GUID (RFC 9562, section 4) gets past `params.parse`, before any query runs (OWASP ASVS 5.0 V1.2.2).
// The hex digits are case-insensitive and the API writes them in lower case, so the parameter is lower-cased: every
// query key for the dispute then has one form.
const guidPattern = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const disputeDetailRoute = createRoute({
  getParentRoute: () => linkedRoute,
  path: '/disputes/$disputeId',
  params: {
    parse: ({ disputeId }) => {
      if (!guidPattern.test(disputeId)) throw notFound();
      return { disputeId: disputeId.toLowerCase() };
    },
  },
  component: DisputeDetailScreen,
});

// 4a and 4b share one parent: it finds the cached row and sends anything else to the list (story 3.6).
const raiseRoute = createRoute({
  getParentRoute: () => linkedRoute,
  path: '/transactions/$transactionId/dispute',
  component: RaiseLayout,
});
const raiseFormRoute = createRoute({
  getParentRoute: () => raiseRoute,
  path: '/',
  component: RaiseScreen,
});
const raiseReviewRoute = createRoute({
  getParentRoute: () => raiseRoute,
  path: 'review',
  component: ReviewScreen,
});

// The SPA applies no redirect rule of its own: the Api accepts only a local URL of at most 2048 characters (IsLocalUrl,
// AD-7; OWASP, Unvalidated Redirects and Forwards). The SPA passes on a string of 1 to 2048 characters, and drops a
// longer one because the Api would answer it with 400 instead of showing the plain sign-in link.
const maxReturnUrlLength = 2048;
function returnUrlOrUndefined(value: unknown): string | undefined {
  if (typeof value !== 'string' || value.length === 0 || value.length > maxReturnUrlLength) {
    return undefined;
  }
  return value;
}

// The session-ended screen: only `?reason=expired` has copy, so any other visit is not-found, for any session state.
const signedOutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/signed-out',
  validateSearch: (search: Record<string, unknown>) => ({
    reason: search.reason === 'expired' ? ('expired' as const) : undefined,
    returnUrl: returnUrlOrUndefined(search.returnUrl),
  }),
  beforeLoad: ({ search }) => {
    if (search.reason !== 'expired') throw notFound();
  },
  component: SignedOutPage,
});

const routeTree = rootRoute.addChildren([
  anonymousRoute.addChildren([startRoute]),
  unlinkedRoute.addChildren([linkRoute, linkOtpRoute]),
  linkedRoute.addChildren([
    transactionsRoute,
    disputesRoute,
    disputeDetailRoute,
    raiseRoute.addChildren([raiseFormRoute, raiseReviewRoute]),
  ]),
  signedOutRoute,
]);

/** The guards read the session through `queryClient`. Tests pass a memory history; the app uses the browser's. */
export function createAppRouter(queryClient: QueryClient, history?: RouterHistory) {
  return createRouter({ routeTree, history, context: { queryClient } });
}
export type AppRouter = ReturnType<typeof createAppRouter>;

declare module '@tanstack/react-router' {
  interface Register {
    router: AppRouter;
  }
  // Set only by the raise screen's navigation after a 201; the detail screen shows its banner (story 3.12).
  interface HistoryState {
    justSubmitted?: boolean;
  }
}
