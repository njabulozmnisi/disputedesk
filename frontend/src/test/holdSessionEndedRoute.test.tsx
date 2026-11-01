import type { AppRouter } from '@/app/router';

import { holdSessionEndedRoute, renderApp, signInAs } from './support';

// The route tree is shared by every app instance in the run, so a test that fails while the route is held must not leave
// it held for the next one. The second test runs after the first has ended without releasing the route.
describe('holdSessionEndedRoute', () => {
  let router: AppRouter;
  let beforeLoad: unknown;

  it('holds the route and the test ends without releasing it', () => {
    signInAs('linked');
    router = renderApp({ path: '/transactions' }).router;
    beforeLoad = router.routesByPath['/signed-out'].options.beforeLoad;

    holdSessionEndedRoute(router);

    expect(router.routesByPath['/signed-out'].options.beforeLoad).not.toBe(beforeLoad);
  });

  it('has put the route back after the test that held it', () => {
    expect(router.routesByPath['/signed-out'].options.beforeLoad).toBe(beforeLoad);
  });
});
