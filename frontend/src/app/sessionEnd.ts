// The login endpoint refuses a returnUrl longer than this (AuthenticationEndpoints, IsLocalUrl).
const maxReturnUrlLength = 2048;

/**
 * "Sign in again" returns to the route the person was on, except from a raise or dispute detail route: its path
 * holds a transaction or dispute ID, which must not reach the sign-in URL, so it returns to the list (AD-31). The
 * path is never the absolute `href`, which the API refuses. The router matches paths case-insensitively, so this
 * compare does too. A path the login endpoint would refuse as too long returns to the start page.
 */
export function sessionEndedUrl(location: { pathname: string; search: string }): string {
  const pathname = location.pathname.toLowerCase();
  let returnUrl = location.pathname + location.search;
  if (pathname.startsWith('/transactions/')) returnUrl = '/transactions';
  else if (pathname.startsWith('/disputes/')) returnUrl = '/disputes';
  else if (returnUrl.length > maxReturnUrlLength) returnUrl = '/';
  return `/signed-out?reason=expired&returnUrl=${encodeURIComponent(returnUrl)}`;
}

let leaving = false;

/**
 * Every session ends here: the browser loads the session-ended screen as a new document, which drops the whole
 * JavaScript heap with the customer's data (HTML Living Standard, navigate; MDN, Location.replace). Nothing is cleaned
 * up in place. `replace` keeps the ended page out of the history. Several things can end a session at once (a tab
 * switch, a 401, the idle timer), and the navigation starts once.
 */
export function leaveForSessionEnded() {
  if (leaving) return;
  leaving = true;
  location.replace(sessionEndedUrl(location));
}

/** For tests: jsdom's `location.replace` does not unload the document, so each test starts as a new document. */
export function startNewDocument() {
  leaving = false;
}
