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
import { RaiseDraftProvider } from '@/features/disputes/raise/RaiseDraft';
import { RaiseLayout } from '@/features/disputes/raise/RaiseLayout';
import { RaiseScreen } from '@/features/disputes/raise/RaiseScreen';
import { ReviewScreen } from '@/features/disputes/raise/ReviewScreen';
import { TransactionsScreen } from '@/features/transactions/TransactionsScreen';

import { RootLayout } from './RootLayout';
import { SessionGate } from './SessionGate';

// Routes are defined with TanStack Router (AD-31). `/`, `/signed-out` and the not-found route are public. A screen
// whose story has not landed (linking, disputes) has no route yet, so its path renders the not-found screen.
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

const transactionsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/transactions',
  component: () => (
    <SessionGate allow="linked">
      <TransactionsScreen />
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
  transactionsRoute,
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
}
