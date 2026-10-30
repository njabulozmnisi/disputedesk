import {
  createRootRoute,
  createRoute,
  createRouter,
  notFound,
  type RouterHistory,
} from '@tanstack/react-router';

import { NotFoundPage } from '@/pages/NotFoundPage';
import { SignedOutPage } from '@/pages/SignedOutPage';
import { StartPage } from '@/pages/StartPage';
import { DisputeDetailScreen } from '@/features/disputes/detail/DisputeDetailScreen';
import { DisputesScreen } from '@/features/disputes/history/DisputesScreen';
import { DetailsScreen } from '@/features/linking/DetailsScreen';
import { OtpScreen } from '@/features/linking/OtpScreen';
import { RaiseDraftProvider } from '@/features/disputes/raise/RaiseDraft';
import { RaiseLayout } from '@/features/disputes/raise/RaiseLayout';
import { RaiseScreen } from '@/features/disputes/raise/RaiseScreen';
import { ReviewScreen } from '@/features/disputes/raise/ReviewScreen';
import { TransactionsScreen } from '@/features/transactions/TransactionsScreen';

import { RootLayout } from './RootLayout';
import { SessionGate } from './SessionGate';

// Routes are defined with TanStack Router (AD-31). `/`, `/signed-out` and the not-found route are public.
// The raise draft lives above every route, so Change keeps its values and Back after a submit finds it submitted.
const rootRoute = createRootRoute({
  component: () => (
    <RaiseDraftProvider>
      <RootLayout />
    </RaiseDraftProvider>
  ),
  notFoundComponent: NotFoundPage,
});

// Anonymous people see the start page; anyone signed in is sent to their state's home.
const startRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: () => (
    <SessionGate allow="anonymous">
      <StartPage />
    </SessionGate>
  ),
});

// 2a and 2b are for an unlinked login only. 2b keeps nothing from 2a: the challenge is held by the server (AD-12).
const linkRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/link',
  component: () => (
    <SessionGate allow="unlinked">
      <DetailsScreen />
    </SessionGate>
  ),
});

const linkOtpRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/link/otp',
  component: () => (
    <SessionGate allow="unlinked">
      <OtpScreen />
    </SessionGate>
  ),
});

const transactionsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/transactions',
  component: () => (
    <SessionGate allow="linked">
      <TransactionsScreen />
    </SessionGate>
  ),
});

const disputesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/disputes',
  component: () => (
    <SessionGate allow="linked">
      <DisputesScreen />
    </SessionGate>
  ),
});

// The router decodes path params and the generated client puts `disputeId` into the request path unencoded, so only
// a canonical GUID (RFC 9562, section 4) gets past `params.parse`, before any query runs (OWASP ASVS 5.0 V1.2.2).
// The hex digits are case-insensitive and the API writes them in lower case, so the parameter is lower-cased: every
// query key for the dispute then has one form.
const guidPattern = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
const disputeDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/disputes/$disputeId',
  params: {
    parse: ({ disputeId }) => {
      if (!guidPattern.test(disputeId)) throw notFound();
      return { disputeId: disputeId.toLowerCase() };
    },
  },
  component: () => (
    <SessionGate allow="linked">
      <DisputeDetailScreen />
    </SessionGate>
  ),
});

// 4a and 4b share one parent: it finds the cached row and sends anything else to the list (story 3.6).
const raiseRoute = createRoute({
  getParentRoute: () => rootRoute,
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

// The BFF accepts a returnUrl of at most 2048 characters, and only a local path (IsLocalUrl, AD-7). The SPA keeps a
// value only when it starts with a single "/" and the browser's own URL parser (WHATWG URL, which drops tab and
// newline and reads a backslash as "/") resolves it to this origin, so what the SPA builds is what a browser follows.
const maxReturnUrlLength = 2048;
function localPathOrUndefined(value: unknown): string | undefined {
  if (typeof value !== 'string' || value.length > maxReturnUrlLength) return undefined;
  if (!value.startsWith('/') || value.startsWith('//')) return undefined;
  // The parser throws on values that pass the checks above but resolve to an invalid host, such as "/\[".
  try {
    if (new URL(value, window.location.origin).origin !== window.location.origin) return undefined;
  } catch {
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
    returnUrl: localPathOrUndefined(search.returnUrl),
  }),
  beforeLoad: ({ search }) => {
    if (search.reason !== 'expired') throw notFound();
  },
  component: SignedOutPage,
});

const routeTree = rootRoute.addChildren([
  startRoute,
  linkRoute,
  linkOtpRoute,
  transactionsRoute,
  disputesRoute,
  disputeDetailRoute,
  raiseRoute.addChildren([raiseFormRoute, raiseReviewRoute]),
  signedOutRoute,
]);

/** Tests pass a memory history; the app uses the browser's. */
export function createAppRouter(history?: RouterHistory) {
  return createRouter({ routeTree, history });
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
